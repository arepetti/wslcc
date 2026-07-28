using System.ComponentModel;
using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;
using Wslcc.Abstractions;
using Wslcc.Client;

namespace Wslcc.Cli.Commands;

/// <summary>
/// <c>wslcc daemon install</c>: registers a per-user autostart (an <c>HKCU\...\Run</c> entry, via
/// <c>reg.exe</c>) that starts <c>wslccd</c> in the user's session every time they sign in. This needs
/// no elevation and no Windows Service, and (for winget installs) it points at the stable
/// <c>…\WinGet\Links\wslccd.exe</c> alias so it keeps working across package upgrades.
/// </summary>
public sealed class DaemonInstallCommand : AsyncCommand<DaemonInstallCommand.Settings>
{
    public sealed class Settings : DaemonProviderSettings
    {
        [CommandOption("--start")]
        [Description("Start the daemon immediately after registering the autostart entry.")]
        public bool Start { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            AnsiConsole.MarkupLine("[red]Autostart registration is only supported on Windows.[/]");
            return 1;
        }

        var daemonPath = DaemonLocator.FindForAutostart();
        if (daemonPath is null)
        {
            AnsiConsole.MarkupLine("[red]Could not locate wslccd.[/] Set the [bold]WSLCCD_PATH[/] environment variable or place wslccd next to wslcc.");
            return 1;
        }

        var executableArgs = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.Provider))
        {
            executableArgs.Add($"--Wslcc:DefaultProvider={settings.Provider}");
        }

        var addArgs = AutostartCommandBuilder.BuildAddArguments(WslccdConstants.AutostartName, daemonPath, executableArgs);
        var addResult = await ProcessRunner.TryRunAsync("reg.exe", addArgs, cancellationToken).ConfigureAwait(false);
        if (addResult is null)
        {
            AnsiConsole.MarkupLine("[red]Could not run reg.exe.[/] Is it available on PATH?");
            return 1;
        }

        if (!addResult.Success)
        {
            AnsiConsole.MarkupLine($"[red]Failed to register autostart[/] (exit code {addResult.ExitCode}).");
            var detail = addResult.StandardError.Length > 0 ? addResult.StandardError : addResult.StandardOutput;
            if (detail.Length > 0)
            {
                AnsiConsole.MarkupLine(detail.Trim().EscapeMarkup());
            }

            return 1;
        }

        AnsiConsole.MarkupLine(
            $"[green]Autostart registered:[/] wslccd starts at logon -> {daemonPath.EscapeMarkup()}");

        if (!settings.Start)
        {
            AnsiConsole.MarkupLine("It starts at your next sign-in. Start it now with [bold]wslcc daemon start[/], or pass [bold]--start[/] next time.");
            return 0;
        }

        return await StartNowAsync(settings, daemonPath, executableArgs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Launches wslccd now (the same way it will be launched at logon) and waits for readiness.</summary>
    private static async Task<int> StartNowAsync(Settings settings, string daemonPath, IReadOnlyList<string> executableArgs, CancellationToken cancellationToken)
    {
        var already = await DaemonClientHelper.TryPingAsync(settings.Host, timeout: TimeSpan.FromSeconds(1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (already is not null)
        {
            AnsiConsole.MarkupLine("[green]Daemon already running.[/]");
            return 0;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = daemonPath,
            WorkingDirectory = Path.GetDirectoryName(daemonPath),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in executableArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to start wslccd:[/] {ex.Message.EscapeMarkup()}");
            return 1;
        }

        var endpoint = WslccEndpoint.Parse(settings.Host);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            var ping = await DaemonClientHelper.TryPingAsync(settings.Host, timeout: TimeSpan.FromSeconds(1), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (ping is not null)
            {
                var providerNote = string.IsNullOrWhiteSpace(ping.DefaultProvider)
                    ? string.Empty
                    : $" Default provider: [bold]{ping.DefaultProvider.EscapeMarkup()}[/].";
                AnsiConsole.MarkupLine($"[green]Daemon started[/] at [bold]{endpoint.Display.EscapeMarkup()}[/] (version {ping.DaemonVersion.EscapeMarkup()}).{providerNote}");
                return 0;
            }
        }

        AnsiConsole.MarkupLine("[yellow]Registered, but the daemon did not become ready in time.[/] Check [bold]wslcc daemon status[/].");
        return 1;
    }
}

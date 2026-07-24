using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;
using Wslcc.Client;

namespace Wslcc.Cli.Commands;

/// <summary>
/// <c>wslcc daemon start</c>: launches <c>wslccd</c> as a detached per-user process and waits for it
/// to accept connections. Only manages a local (named-pipe) daemon.
/// </summary>
public sealed class DaemonStartCommand : AsyncCommand<DaemonProviderSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, DaemonProviderSettings settings, CancellationToken cancellationToken)
    {
        var endpoint = WslccEndpoint.Parse(settings.Host);
        if (!endpoint.IsNamedPipe)
        {
            AnsiConsole.MarkupLine("[red]'daemon start' manages a local daemon only.[/] A http(s) --host targets a remote daemon.");
            return 1;
        }

        var already = await DaemonClientHelper.TryPingAsync(settings.Host, timeout: TimeSpan.FromSeconds(1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (already is not null)
        {
            AnsiConsole.MarkupLine($"[green]Daemon already running[/] at [bold]{endpoint.Display.EscapeMarkup()}[/] (version {already.DaemonVersion.EscapeMarkup()}).");

            if (!string.IsNullOrWhiteSpace(settings.Provider)
                && !string.Equals(settings.Provider, already.DefaultProvider, StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]Warning:[/] requested provider [bold]{settings.Provider.EscapeMarkup()}[/] differs from the running daemon's default [bold]{already.DefaultProvider.EscapeMarkup()}[/]. "
                    + "Restart the daemon ([bold]wslcc daemon stop[/] then [bold]wslcc daemon start --provider "
                    + $"{settings.Provider.EscapeMarkup()}[/]) to change it, or pass [bold]--provider[/] per command.");
            }

            return 0;
        }

        var daemonPath = DaemonLocator.Find();
        if (daemonPath is null)
        {
            AnsiConsole.MarkupLine("[red]Could not locate wslccd.[/] Set the [bold]WSLCCD_PATH[/] environment variable or place wslccd next to wslcc.");
            return 1;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = daemonPath,
            WorkingDirectory = Path.GetDirectoryName(daemonPath),
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Persist the chosen provider as the daemon's default so subsequent commands need not repeat
        // --provider. Bound into configuration via the command line (section 'Wslcc').
        if (!string.IsNullOrWhiteSpace(settings.Provider))
        {
            startInfo.ArgumentList.Add($"--Wslcc:DefaultProvider={settings.Provider}");
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

        AnsiConsole.MarkupLine("[red]Timed out waiting for the daemon to become ready.[/]");
        return 1;
    }
}

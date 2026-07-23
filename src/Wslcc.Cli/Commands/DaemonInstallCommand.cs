using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Wslcc.Abstractions;

namespace Wslcc.Cli.Commands;

/// <summary>
/// <c>wslcc daemon install</c>: registers <c>wslccd</c> as a Windows Service via <c>sc.exe create</c>, so
/// it can start automatically at boot and run without a signed-in user. Requires an elevated
/// (Administrator) prompt.
/// </summary>
public sealed class DaemonInstallCommand : AsyncCommand<DaemonInstallCommand.Settings>
{
    private static readonly IReadOnlyDictionary<string, string> StartupTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["auto"] = "auto",
        ["manual"] = "demand",
        ["disabled"] = "disabled",
    };

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--startup <TYPE>")]
        [Description("Service startup type: 'auto' (default), 'manual', or 'disabled'.")]
        public string Startup { get; set; } = "auto";

        [CommandOption("--start")]
        [Description("Start the service immediately after registering it.")]
        public bool Start { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            AnsiConsole.MarkupLine("[red]Windows Service registration is only supported on Windows.[/]");
            return 1;
        }

        if (!StartupTypes.TryGetValue(settings.Startup, out var scStartupType))
        {
            AnsiConsole.MarkupLine($"[red]Unknown --startup value '{settings.Startup.EscapeMarkup()}'.[/] Use 'auto', 'manual', or 'disabled'.");
            return 1;
        }

        var daemonPath = DaemonLocator.Find();
        if (daemonPath is null)
        {
            AnsiConsole.MarkupLine("[red]Could not locate wslccd.[/] Set the [bold]WSLCCD_PATH[/] environment variable or place wslccd next to wslcc.");
            return 1;
        }

        var alreadyRunning = await DaemonClientHelper.TryPingAsync(settings.Host, timeout: TimeSpan.FromSeconds(1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (alreadyRunning is not null)
        {
            AnsiConsole.MarkupLine(
                "[yellow]Warning:[/] a per-user daemon is already running (started via [bold]wslcc daemon start[/]). "
                + "It will conflict with the service on the same named pipe; stop it first with [bold]wslcc daemon stop[/].");
        }

        var executableArgs = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.Provider))
        {
            executableArgs.Add($"--Wslcc:DefaultProvider={settings.Provider}");
        }

        var createArgs = ServiceControlCommandBuilder.BuildCreateArguments(
            WslccdConstants.ServiceName, daemonPath, executableArgs, scStartupType, WslccdConstants.ServiceName);
        var createResult = await ProcessRunner.TryRunAsync("sc.exe", createArgs, cancellationToken).ConfigureAwait(false);

        if (!TryReportFailure(createResult, "register"))
        {
            return 1;
        }

        AnsiConsole.MarkupLine(
            $"[green]Service registered:[/] [bold]{WslccdConstants.ServiceName.EscapeMarkup()}[/] ({settings.Startup.EscapeMarkup()}) -> {daemonPath.EscapeMarkup()}");

        // Best-effort: a friendly description in services.msc. Not fatal if it fails.
        var descriptionArgs = ServiceControlCommandBuilder.BuildDescriptionArguments(WslccdConstants.ServiceName, "WSLCC container orchestration daemon.");
        await ProcessRunner.TryRunAsync("sc.exe", descriptionArgs, cancellationToken).ConfigureAwait(false);

        if (!settings.Start)
        {
            AnsiConsole.MarkupLine($"Start it with [bold]wslcc daemon start[/] (or [bold]sc start \"{WslccdConstants.ServiceName}\"[/]), or pass [bold]--start[/] next time.");
            return 0;
        }

        var startResult = await ProcessRunner.TryRunAsync("sc.exe", $"start \"{WslccdConstants.ServiceName}\"", cancellationToken).ConfigureAwait(false);
        if (!TryReportFailure(startResult, "start"))
        {
            return 1;
        }

        AnsiConsole.MarkupLine("[green]Service started.[/]");
        return 0;
    }

    /// <summary>Prints a friendly error for a failed <c>sc.exe</c> invocation. Returns <c>true</c> on success.</summary>
    private static bool TryReportFailure(ProcessResult? result, string verb)
    {
        if (result is null)
        {
            AnsiConsole.MarkupLine("[red]Could not run sc.exe.[/] Is it available on PATH?");
            return false;
        }

        if (result.Success)
        {
            return true;
        }

        AnsiConsole.MarkupLine($"[red]Failed to {verb} the service[/] (exit code {result.ExitCode}).");
        switch (result.ExitCode)
        {
            case 5:
                AnsiConsole.MarkupLine("Access denied - re-run from an [bold]elevated[/] (Administrator) prompt.");
                break;
            case 1073:
                AnsiConsole.MarkupLine($"Already registered. Run [bold]wslcc daemon uninstall[/] first to change its settings.");
                break;
            default:
                var detail = result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput;
                if (detail.Length > 0)
                {
                    AnsiConsole.MarkupLine(detail.Trim().EscapeMarkup());
                }

                break;
        }

        return false;
    }
}

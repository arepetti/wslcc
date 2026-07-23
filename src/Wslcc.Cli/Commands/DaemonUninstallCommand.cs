using Spectre.Console;
using Spectre.Console.Cli;
using Wslcc.Abstractions;

namespace Wslcc.Cli.Commands;

/// <summary>
/// <c>wslcc daemon uninstall</c>: stops (if running) and removes the <c>wslccd</c> Windows Service
/// registered by <c>wslcc daemon install</c>. Requires an elevated (Administrator) prompt.
/// </summary>
public sealed class DaemonUninstallCommand : AsyncCommand<GlobalSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            AnsiConsole.MarkupLine("[red]Windows Service registration is only supported on Windows.[/]");
            return 1;
        }

        // Best-effort stop; a service that is not running or not installed fails here too, but the
        // delete below reports the actual outcome (including "not installed").
        var stopArgs = ServiceControlCommandBuilder.BuildStopArguments(WslccdConstants.ServiceName);
        var stopResult = await ProcessRunner.TryRunAsync("sc.exe", stopArgs, cancellationToken).ConfigureAwait(false);
        if (stopResult is { Success: true })
        {
            AnsiConsole.MarkupLine("[green]Service stopped.[/]");
        }

        var deleteArgs = ServiceControlCommandBuilder.BuildDeleteArguments(WslccdConstants.ServiceName);
        var deleteResult = await ProcessRunner.TryRunAsync("sc.exe", deleteArgs, cancellationToken).ConfigureAwait(false);

        if (deleteResult is null)
        {
            AnsiConsole.MarkupLine("[red]Could not run sc.exe.[/] Is it available on PATH?");
            return 1;
        }

        if (deleteResult.Success)
        {
            AnsiConsole.MarkupLine($"[green]Service removed:[/] [bold]{WslccdConstants.ServiceName.EscapeMarkup()}[/].");
            return 0;
        }

        if (deleteResult.ExitCode == 1060)
        {
            AnsiConsole.MarkupLine($"[yellow]Not installed:[/] no [bold]{WslccdConstants.ServiceName.EscapeMarkup()}[/] service found.");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]Failed to remove the service[/] (exit code {deleteResult.ExitCode}).");
        if (deleteResult.ExitCode == 5)
        {
            AnsiConsole.MarkupLine("Access denied - re-run from an [bold]elevated[/] (Administrator) prompt.");
        }
        else
        {
            var detail = deleteResult.StandardError.Length > 0 ? deleteResult.StandardError : deleteResult.StandardOutput;
            if (detail.Length > 0)
            {
                AnsiConsole.MarkupLine(detail.Trim().EscapeMarkup());
            }
        }

        return 1;
    }
}

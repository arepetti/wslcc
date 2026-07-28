using Spectre.Console;
using Spectre.Console.Cli;
using Wslcc.Abstractions;

namespace Wslcc.Cli.Commands;

/// <summary>
/// <c>wslcc daemon uninstall</c>: removes the per-user autostart entry (<c>HKCU\...\Run</c>) registered
/// by <c>wslcc daemon install</c>. It does not stop an already-running daemon — use
/// <c>wslcc daemon stop</c> for that. Needs no elevation.
/// </summary>
public sealed class DaemonUninstallCommand : AsyncCommand<GlobalSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            AnsiConsole.MarkupLine("[red]Autostart registration is only supported on Windows.[/]");
            return 1;
        }

        var deleteArgs = AutostartCommandBuilder.BuildDeleteArguments(WslccdConstants.AutostartName);
        var deleteResult = await ProcessRunner.TryRunAsync("reg.exe", deleteArgs, cancellationToken).ConfigureAwait(false);

        if (deleteResult is null)
        {
            AnsiConsole.MarkupLine("[red]Could not run reg.exe.[/] Is it available on PATH?");
            return 1;
        }

        if (deleteResult.Success)
        {
            AnsiConsole.MarkupLine("[green]Autostart removed.[/]");
            AnsiConsole.MarkupLine("A daemon that is already running keeps running; stop it with [bold]wslcc daemon stop[/].");
            return 0;
        }

        // reg.exe returns a non-zero exit code both when the value is absent and on real failures;
        // treat the "cannot find" / "unable to find" message as success (nothing to remove).
        var detail = deleteResult.StandardError.Length > 0 ? deleteResult.StandardError : deleteResult.StandardOutput;
        if (detail.Contains("unable to find", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[yellow]Not installed:[/] no autostart entry found.");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]Failed to remove autostart[/] (exit code {deleteResult.ExitCode}).");
        if (detail.Length > 0)
        {
            AnsiConsole.MarkupLine(detail.Trim().EscapeMarkup());
        }

        return 1;
    }
}

using Spectre.Console;
using Spectre.Console.Cli;

namespace Wslcc.Cli.Commands;

/// <summary>
/// Placeholder for a Compose verb that is not implemented yet. Registered under multiple names so
/// the command tree and help mirror <c>docker compose</c> from day one.
/// </summary>
public sealed class ComposeStubCommand : Command<GlobalSettings>
{
    protected override int Execute(CommandContext context, GlobalSettings settings, CancellationToken cancellationToken)
    {
        var verb = context.Name;
        AnsiConsole.MarkupLine($"[yellow]'wslcc compose {verb.EscapeMarkup()}' is not implemented yet.[/]");
        AnsiConsole.MarkupLine("[grey]Tracked in docs/todo.md. Try [bold]wslcc compose version[/] or [bold]wslcc version[/].[/]");
        return 1;
    }
}

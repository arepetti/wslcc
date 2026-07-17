using Spectre.Console;
using Spectre.Console.Cli;
using Wslcc.Client;

namespace Wslcc.Cli.Commands;

/// <summary><c>wslcc daemon status</c>: reports whether the daemon responds and on which endpoint.</summary>
public sealed class DaemonStatusCommand : AsyncCommand<GlobalSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings, CancellationToken cancellationToken)
    {
        var endpoint = WslccEndpoint.Parse(settings.Host);
        var ping = await DaemonClientHelper.TryPingAsync(settings.Host, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (ping is null)
        {
            AnsiConsole.MarkupLine($"[yellow]Daemon not running[/] at [bold]{endpoint.Display.EscapeMarkup()}[/].");
            AnsiConsole.MarkupLine("Start it with [bold]wslcc daemon start[/].");
            return 1;
        }

        var providerNote = string.IsNullOrWhiteSpace(ping.DefaultProvider)
            ? string.Empty
            : $" Default provider: [bold]{ping.DefaultProvider.EscapeMarkup()}[/].";
        AnsiConsole.MarkupLine(
            $"[green]Daemon running[/] at [bold]{endpoint.Display.EscapeMarkup()}[/] (version {ping.DaemonVersion.EscapeMarkup()}).{providerNote}");
        return 0;
    }
}

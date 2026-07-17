using Spectre.Console;
using Spectre.Console.Cli;
using Wslcc.Client;

namespace Wslcc.Cli.Commands;

/// <summary><c>wslcc daemon stop</c>: asks the daemon to shut down gracefully via the Shutdown RPC.</summary>
public sealed class DaemonStopCommand : AsyncCommand<GlobalSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings, CancellationToken cancellationToken)
    {
        var endpoint = WslccEndpoint.Parse(settings.Host);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            using var client = new WslccClient(settings.Host);
            await client.ShutdownAsync(cts.Token).ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[green]Daemon stopping[/] at [bold]{endpoint.Display.EscapeMarkup()}[/].");
            return 0;
        }
        catch (Exception ex) when (DaemonClientHelper.IsConnectionError(ex))
        {
            AnsiConsole.MarkupLine($"[yellow]Daemon not running[/] at [bold]{endpoint.Display.EscapeMarkup()}[/].");
            return 0;
        }
    }
}

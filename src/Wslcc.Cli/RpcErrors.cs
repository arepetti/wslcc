using Grpc.Core;
using Spectre.Console;

namespace Wslcc.Cli;

internal static class RpcErrors
{
    /// <summary>Prints a friendly message for an RPC failure and returns a non-zero exit code.</summary>
    public static int Report(RpcException ex)
    {
        if (Environment.GetEnvironmentVariable("WSLCC_DEBUG") == "1")
        {
            AnsiConsole.WriteException(ex);
        }

        if (DaemonClientHelper.IsConnectionError(ex))
        {
            AnsiConsole.MarkupLine("[yellow]Daemon not reachable.[/] Start it with [bold]wslcc daemon start[/].");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Status.Detail.EscapeMarkup()}");
        }

        return 1;
    }

    public static string ShortId(string id) => id.Length > 12 ? id.Substring(0, 12) : id;
}

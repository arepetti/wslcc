using Spectre.Console;
using Wslcc.Client;
using Wslcc.Grpc.Contracts;

namespace Wslcc.Cli;

/// <summary>
/// Shared rendering for streamed compose log output, used by both <c>compose logs</c> and the attached
/// (foreground) mode of <c>compose up</c> so their output stays identical.
/// </summary>
internal static class LogStreaming
{
    /// <summary>
    /// Streams log lines from the daemon to the console until the stream ends or
    /// <paramref name="cancellationToken"/> is cancelled. When <paramref name="showTimestamps"/> is set,
    /// each line is prefixed with its timestamp. Returns whether any line was printed.
    /// </summary>
    public static async Task<bool> RenderAsync(WslccClient client, LogsRequest request, bool showTimestamps, CancellationToken cancellationToken)
    {
        var any = false;
        await foreach (var line in client.GetLogsAsync(request, cancellationToken).ConfigureAwait(false))
        {
            any = true;
            var prefix = showTimestamps && !string.IsNullOrEmpty(line.Timestamp)
                ? $"[grey]{line.Timestamp.EscapeMarkup()}[/] "
                : string.Empty;
            AnsiConsole.MarkupLine($"[grey]{line.Service.EscapeMarkup()}[/] | {prefix}{line.Line.EscapeMarkup()}");
        }

        return any;
    }
}

using System.Globalization;
using Wslcc.Abstractions;

namespace Wslcc.Providers.Common;

/// <summary>
/// Parses the output of a container CLI's <c>logs --timestamps</c>, which prefixes each line with an
/// RFC3339(Nano) timestamp and a space (<c>"2024-05-01T12:34:56.789012345Z the message"</c>). Kept pure
/// and separate from the process plumbing so it can be unit-tested.
/// </summary>
public static class CliLogParser
{
    /// <summary>
    /// Splits the leading timestamp off a <c>--timestamps</c> log line. Falls back to a
    /// <c>null</c> timestamp with the whole line as the message when no valid prefix is present (e.g. a
    /// continuation line the runtime did not stamp).
    /// </summary>
    /// <param name="raw">One raw output line from a <c>logs --timestamps</c> invocation.</param>
    /// <returns>The line's timestamp and message, or a <c>null</c> timestamp and the whole line.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="raw"/> is <c>null</c>.</exception>
    public static ContainerLogLine ParseTimestamped(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var space = raw.IndexOf(' ');
        if (space <= 0)
            return new ContainerLogLine(null, raw);

        if (!TryParseTimestamp(raw.AsSpan(0, space), out var timestamp))
            return new ContainerLogLine(null, raw);

        return new ContainerLogLine(timestamp, raw[(space + 1)..]);
    }

    private static bool TryParseTimestamp(ReadOnlySpan<char> value, out DateTimeOffset timestamp)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal,
            out timestamp);
}

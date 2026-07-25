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
    public static ContainerLogLine ParseTimestamped(string raw)
    {
        var space = raw.IndexOf(' ');
        if (space > 0
            && DateTimeOffset.TryParse(
                raw.AsSpan(0, space),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal,
                out var timestamp))
        {
            return new ContainerLogLine(timestamp, raw[(space + 1)..]);
        }

        return new ContainerLogLine(null, raw);
    }
}

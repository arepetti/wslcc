namespace Wslcc.Abstractions;

/// <summary>
/// A single log line from a project's container, tagged with the owning service name.
/// <see cref="Timestamp"/> carries the line's time (UTC) when timestamps were requested, for display
/// and for timestamp-ordered merging of a bounded dump; it is <c>null</c> otherwise.
/// </summary>
/// <param name="Service">Compose service the line came from.</param>
/// <param name="Line">The log text.</param>
/// <param name="Timestamp">The line's time when timestamps were requested; <c>null</c> otherwise.</param>
/// <exception cref="ArgumentException"><paramref name="Service"/> is null, empty or whitespace.</exception>
/// <exception cref="ArgumentNullException"><paramref name="Line"/> is <c>null</c>.</exception>
public sealed record ServiceLogLine(string Service, string Line, DateTimeOffset? Timestamp = null)
{
    /// <summary>Compose service the line came from.</summary>
    public string Service { get; init; } = RequireService(Service);

    /// <summary>The log text; a blank line is valid output.</summary>
    public string Line { get; init; } = RequireLine(Line);

    private static string RequireService(string service)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        return service;
    }

    private static string RequireLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line;
    }
}

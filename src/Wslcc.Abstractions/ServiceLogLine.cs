namespace Wslcc.Abstractions;

/// <summary>
/// A single log line from a project's container, tagged with the owning service name.
/// <see cref="Timestamp"/> carries the line's time (UTC) when timestamps were requested, for display
/// and for timestamp-ordered merging of a bounded dump; it is <c>null</c> otherwise.
/// </summary>
public sealed record ServiceLogLine(string Service, string Line, DateTimeOffset? Timestamp = null);

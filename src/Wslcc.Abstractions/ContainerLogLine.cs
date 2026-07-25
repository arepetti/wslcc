namespace Wslcc.Abstractions;

/// <summary>
/// A single line from a container's log stream. <see cref="Timestamp"/> is populated when the log was
/// requested with timestamps (used both for display and for timestamp-ordered merging across
/// containers); otherwise it is <c>null</c> and <see cref="Message"/> is the raw line.
/// </summary>
public sealed record ContainerLogLine(DateTimeOffset? Timestamp, string Message);

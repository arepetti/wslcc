namespace Wslcc.Abstractions;

/// <summary>
/// A single line from a container's log stream. <see cref="Timestamp"/> is populated when the log was
/// requested with timestamps (used both for display and for timestamp-ordered merging across
/// containers); otherwise it is <c>null</c> and <see cref="Message"/> is the raw line.
/// </summary>
/// <param name="Timestamp">The line's time when timestamps were requested; <c>null</c> otherwise.</param>
/// <param name="Message">The log text, with the timestamp prefix already stripped when there was one.</param>
public sealed record ContainerLogLine(DateTimeOffset? Timestamp, string Message);

namespace Wslcc.Abstractions;

/// <summary>A single log line from a project's container, tagged with the owning service name.</summary>
public sealed record ServiceLogLine(string Service, string Line);

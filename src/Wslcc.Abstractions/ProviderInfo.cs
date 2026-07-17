namespace Wslcc.Abstractions;

/// <summary>
/// Describes a container provider and the version/availability of the tooling it depends on.
/// </summary>
public sealed record ProviderInfo(
    string Name,
    string DisplayName,
    bool IsAvailable,
    string? Version = null,
    string? Details = null);

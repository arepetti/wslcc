namespace Wslcc.Abstractions;

/// <summary>
/// A container as reported by a provider.
/// </summary>
public sealed record ContainerInfo(
    string Id,
    string Name,
    string Image,
    string State,
    string? Status = null,
    string? Service = null,
    string? Ports = null,
    string? Project = null,
    string? ConfigHash = null);

namespace Wslcc.Abstractions;

/// <summary>
/// Describes a container provider and the version/availability of the tooling it depends on.
/// </summary>
/// <param name="Name">Stable identifier used on the command line (matches <see cref="IContainerProvider.Name"/>).</param>
/// <param name="DisplayName">Human-readable name for output.</param>
/// <param name="IsAvailable">Whether the underlying tooling was found and is usable.</param>
/// <param name="Version">Version reported by the tooling, when available.</param>
/// <param name="Details">Extra detail, typically the reason the provider is unavailable.</param>
public sealed record ProviderInfo(
    string Name,
    string DisplayName,
    bool IsAvailable,
    string? Version = null,
    string? Details = null);

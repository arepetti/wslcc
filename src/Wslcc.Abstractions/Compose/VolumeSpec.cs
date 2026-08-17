namespace Wslcc.Abstractions.Compose;

/// <summary>
/// A volume definition from the <c>volumes:</c> section.
/// </summary>
public sealed class VolumeSpec
{
    /// <summary>Key as written under <c>volumes:</c> (not yet project-prefixed).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Compose <c>driver:</c>; <c>null</c> leaves the provider's default.</summary>
    public string? Driver { get; set; }

    /// <summary>Compose <c>external: true</c> — the volume already exists and must not be created or removed.</summary>
    public bool External { get; set; }
}

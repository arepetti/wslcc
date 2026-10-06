namespace Wslcc.Abstractions.Compose;

/// <summary>
/// A network definition from the <c>networks:</c> section.
/// </summary>
public sealed class NetworkSpec
{
    /// <summary>Key as written under <c>networks:</c> (not yet project-prefixed).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Compose <c>driver:</c>; <c>null</c> leaves the provider's default.</summary>
    public string? Driver { get; set; }

    public IDictionary<string, string> DriverOptions { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public bool Internal { get; set; }

    public string? Subnet { get; set; }

    public string? Gateway { get; set; }

    public string? IpRange { get; set; }

    /// <summary>Compose <c>external: true</c> — the network already exists and must not be created or removed.</summary>
    public bool External { get; set; }
}

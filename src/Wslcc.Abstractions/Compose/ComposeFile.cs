namespace Wslcc.Abstractions.Compose;

/// <summary>
/// In-memory representation of a Compose file (the subset WSLCC currently understands).
/// </summary>
public sealed class ComposeFile
{
    /// <summary>Optional top-level project name (Compose <c>name:</c>).</summary>
    public string? Name { get; set; }

    public Dictionary<string, ServiceSpec> Services { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, NetworkSpec> Networks { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, VolumeSpec> Volumes { get; set; } = new(StringComparer.Ordinal);
}

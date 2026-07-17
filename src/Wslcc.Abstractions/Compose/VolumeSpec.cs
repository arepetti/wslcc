namespace Wslcc.Abstractions.Compose;

/// <summary>
/// A volume definition from the <c>volumes:</c> section.
/// </summary>
public sealed class VolumeSpec
{
    public string Name { get; set; } = string.Empty;

    public string? Driver { get; set; }

    public bool External { get; set; }
}

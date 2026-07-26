namespace Wslcc.Abstractions;

/// <summary>
/// A provider-agnostic description of a named volume to ensure exists before starting containers. The
/// compose engine derives these from the compose <c>volumes:</c> section; providers translate them into
/// their CLI/API calls.
/// </summary>
public sealed class VolumeCreateSpec
{
    public string Name { get; set; } = string.Empty;

    public string? Driver { get; set; }

    public IDictionary<string, string> Labels { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

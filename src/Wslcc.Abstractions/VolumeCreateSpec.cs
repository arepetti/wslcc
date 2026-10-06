namespace Wslcc.Abstractions;

/// <summary>
/// A provider-agnostic description of a named volume to ensure exists before starting containers. The
/// compose engine derives these from the compose <c>volumes:</c> section; providers translate them into
/// their CLI/API calls.
/// </summary>
public sealed class VolumeCreateSpec
{
    /// <summary>Volume name as the provider will see it, already project-prefixed (<see cref="WslccLabels.VolumeName"/>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Volume driver to use; <c>null</c> leaves the provider's default.</summary>
    public string? Driver { get; set; }

    public IDictionary<string, string> DriverOptions { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Labels applied at creation time, including the <see cref="WslccLabels.Project"/> key that lets
    /// <c>down --volumes</c> find the volumes wslcc created for the project.
    /// </summary>
    public IDictionary<string, string> Labels { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

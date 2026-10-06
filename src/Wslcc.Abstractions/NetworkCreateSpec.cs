namespace Wslcc.Abstractions;

/// <summary>
/// A provider-agnostic description of a project network to ensure exists before starting containers.
/// The compose engine derives these from the compose <c>networks:</c> section (and the implicit default
/// network); providers translate them into their CLI/API calls.
/// </summary>
public sealed class NetworkCreateSpec
{
    /// <summary>Network name as the provider will see it, already project-prefixed (<see cref="WslccLabels.NetworkName"/>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Network driver to use; <c>null</c> leaves the provider's default.</summary>
    public string? Driver { get; set; }

    public IDictionary<string, string> DriverOptions { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public bool Internal { get; set; }

    public string? Subnet { get; set; }

    public string? Gateway { get; set; }

    public string? IpRange { get; set; }

    /// <summary>
    /// Labels applied at creation time, including the <see cref="WslccLabels.Project"/> key that lets
    /// <c>down</c> find the networks wslcc created for the project.
    /// </summary>
    public IDictionary<string, string> Labels { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

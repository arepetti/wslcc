namespace Wslcc.Abstractions;

/// <summary>
/// A provider-agnostic description of a container to create and start. The compose engine builds one
/// of these per service; providers translate it into their CLI/API calls.
/// </summary>
public sealed class ContainerRunSpec
{
    public string Image { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public IDictionary<string, string> Labels { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public IDictionary<string, string?> Environment { get; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>Raw port mappings in Compose form, e.g. "8080:80".</summary>
    public IList<string> Ports { get; } = new List<string>();

    public IList<string> Command { get; } = new List<string>();

    public string? Restart { get; set; }

    /// <summary>Optional healthcheck to apply to the container (from the service's <c>healthcheck:</c>).</summary>
    public ContainerHealthCheck? HealthCheck { get; set; }

    /// <summary>Run detached (default true for compose up).</summary>
    public bool Detach { get; set; } = true;
}

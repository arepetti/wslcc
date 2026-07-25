namespace Wslcc.Abstractions.Compose;

/// <summary>
/// A single service definition from the <c>services:</c> section of a Compose file.
/// </summary>
public sealed class ServiceSpec
{
    /// <summary>Service key as written under <c>services:</c>.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Image { get; set; }

    public BuildSpec? Build { get; set; }

    public string? ContainerName { get; set; }

    public IList<string> Command { get; set; } = new List<string>();

    public IList<string> Entrypoint { get; set; } = new List<string>();

    public IDictionary<string, string?> Environment { get; set; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    public IList<string> EnvFile { get; set; } = new List<string>();

    public IList<string> Ports { get; set; } = new List<string>();

    public IList<string> Volumes { get; set; } = new List<string>();

    public IList<ServiceDependency> DependsOn { get; set; } = new List<ServiceDependency>();

    public HealthCheckSpec? HealthCheck { get; set; }

    public IList<string> Networks { get; set; } = new List<string>();

    public IDictionary<string, string> Labels { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public string? Restart { get; set; }

    public string? WorkingDir { get; set; }

    public string? User { get; set; }
}

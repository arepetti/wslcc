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

    /// <summary>
    /// Container argv override. A Compose string <c>command:</c> is expanded by the parser into
    /// <c>/bin/sh</c>, <c>-c</c>, and the original string (shell form); a list is kept as exec-form tokens.
    /// </summary>
    public IList<string> Command { get; set; } = new List<string>();

    /// <summary>
    /// Image entrypoint override. A Compose string <c>entrypoint:</c> is expanded by the parser into
    /// shell form (<c>/bin/sh -c</c>); a list is kept as exec-form tokens.
    /// </summary>
    public IList<string> Entrypoint { get; set; } = new List<string>();

    public IDictionary<string, string?> Environment { get; set; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>Files whose variables are loaded into the container (before <see cref="Environment"/> overrides).</summary>
    public IList<EnvFileSpec> EnvFile { get; set; } = new List<EnvFileSpec>();

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

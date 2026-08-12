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

    /// <summary>
    /// Absolute (or otherwise provider-readable) paths passed as <c>--env-file</c>. Applied before
    /// <see cref="Environment"/> so <c>environment:</c> keys win.
    /// </summary>
    public IList<string> EnvFiles { get; } = new List<string>();

    /// <summary>Raw port mappings in Compose form, e.g. "8080:80".</summary>
    public IList<string> Ports { get; } = new List<string>();

    /// <summary>
    /// Resolved volume mounts in <c>docker run -v</c> form (e.g. "myproject_data:/var/lib",
    /// "/host/path:/app:ro", or an anonymous "/data"). Named volumes are already project-prefixed and
    /// relative bind sources already resolved by the engine.
    /// </summary>
    public IList<string> Volumes { get; } = new List<string>();

    /// <summary>Network the container is attached to at creation time. Additional networks are connected afterward.</summary>
    public string? Network { get; set; }

    /// <summary>Network alias to publish on <see cref="Network"/> (usually the service name).</summary>
    public string? NetworkAlias { get; set; }

    /// <summary>
    /// Entrypoint override (exec argv). The first token becomes <c>--entrypoint</c>; any further
    /// tokens are placed after the image (before <see cref="Command"/>), matching <c>docker run</c>.
    /// </summary>
    public IList<string> Entrypoint { get; } = new List<string>();

    public IList<string> Command { get; } = new List<string>();

    /// <summary>Passed as <c>-u</c>/<c>--user</c> when set.</summary>
    public string? User { get; set; }

    /// <summary>Passed as <c>-w</c>/<c>--workdir</c> when set.</summary>
    public string? WorkingDir { get; set; }

    public string? Restart { get; set; }

    /// <summary>Optional healthcheck to apply to the container (from the service's <c>healthcheck:</c>).</summary>
    public ContainerHealthCheck? HealthCheck { get; set; }

    /// <summary>Run detached (default true for compose up).</summary>
    public bool Detach { get; set; } = true;
}

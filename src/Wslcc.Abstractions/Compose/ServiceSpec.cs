namespace Wslcc.Abstractions.Compose;

/// <summary>
/// A single service definition from the <c>services:</c> section of a Compose file.
/// </summary>
/// <remarks>
/// The parser normalizes the short and long forms of each key into these properties, so consumers see
/// one shape regardless of how the service was written. Values are kept as they appear in the document:
/// paths are not resolved and names are not project-prefixed — the engine does that when it builds the
/// provider-level specs.
/// </remarks>
/// <example>
/// The service written as
/// <code>
/// services:
///   web:
///     image: nginx:alpine
///     ports: ["8080:80"]
///     depends_on:
///       db:
///         condition: service_healthy
/// </code>
/// is read as:
/// <code>
/// ServiceSpec web = file.Services["web"];
/// // web.Image     == "nginx:alpine"
/// // web.Ports     == ["8080:80"]
/// // web.DependsOn == [new ServiceDependency("db", DependencyCondition.ServiceHealthy)]
/// </code>
/// </example>
public sealed class ServiceSpec
{
    /// <summary>Service key as written under <c>services:</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Compose <c>image:</c>. May be <c>null</c> for a build-only service.</summary>
    public string? Image { get; set; }

    /// <summary>Compose <c>build:</c>, or <c>null</c> when the service only uses a prebuilt image.</summary>
    public BuildSpec? Build { get; set; }

    /// <summary>Compose <c>container_name:</c> — an explicit name overriding the generated one.</summary>
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

    /// <summary>
    /// Compose <c>environment:</c>, from either the map or the <c>KEY=VALUE</c> list form. A <c>null</c>
    /// value is a name with no value, inherited from the host.
    /// </summary>
    public IDictionary<string, string?> Environment { get; set; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>Files whose variables are loaded into the container (before <see cref="Environment"/> overrides).</summary>
    public IList<EnvFileSpec> EnvFile { get; set; } = new List<EnvFileSpec>();

    /// <summary>Compose <c>ports:</c> in short syntax (e.g. <c>"8080:80"</c>); the long map form is not supported.</summary>
    public IList<string> Ports { get; set; } = new List<string>();

    /// <summary>
    /// Compose <c>volumes:</c> (short or long form) and service <c>tmpfs:</c>, as structured mounts.
    /// Sources are unresolved: named volumes are not yet project-prefixed and relative bind sources
    /// not yet rooted.
    /// </summary>
    public IList<ServiceMount> Volumes { get; set; } = new List<ServiceMount>();

    /// <summary>Compose <c>depends_on:</c>, normalized from both the list and map forms.</summary>
    public IList<ServiceDependency> DependsOn { get; set; } = new List<ServiceDependency>();

    /// <summary>Compose <c>healthcheck:</c>, or <c>null</c> when the service declares none.</summary>
    public HealthCheckSpec? HealthCheck { get; set; }

    /// <summary>Names of the <c>networks:</c> the service joins; empty means the project's default network.</summary>
    public IList<string> Networks { get; set; } = new List<string>();

    /// <summary>Compose <c>labels:</c> applied to the container, alongside the wslcc labels the engine adds.</summary>
    public IDictionary<string, string> Labels { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Compose <c>restart:</c> policy (e.g. <c>always</c>, <c>unless-stopped</c>).</summary>
    public string? Restart { get; set; }

    /// <summary>Compose <c>working_dir:</c> — the container's working directory.</summary>
    public string? WorkingDir { get; set; }

    /// <summary>Compose <c>user:</c> — the user (and optionally group) the container process runs as.</summary>
    public string? User { get; set; }

    /// <summary>Compose <c>hostname:</c> — the container's hostname (UTS name).</summary>
    public string? Hostname { get; set; }

    /// <summary>Compose <c>read_only:</c> — mount the container root filesystem read-only.</summary>
    public bool ReadOnly { get; set; }
}

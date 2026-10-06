using Wslcc.Abstractions.Compose;

namespace Wslcc.Abstractions;

/// <summary>
/// A provider-agnostic description of a container to create and start. The compose engine builds one
/// of these per service; providers translate it into their CLI/API calls.
/// </summary>
/// <remarks>
/// Collection properties are read-only and pre-initialized: add to them rather than assigning. Every
/// path and name is expected to be already resolved (project-prefixed volumes, absolute bind sources,
/// absolute env-file paths); providers pass them through verbatim.
/// </remarks>
/// <example>
/// A web service on the project network, publishing a port and mounting a named volume:
/// <code>
/// var spec = new ContainerRunSpec
/// {
///     Image = "nginx:alpine",
///     Name = WslccLabels.ContainerName("myproject", "web"),
///     Network = WslccLabels.DefaultNetworkName("myproject"),
///     NetworkAlias = "web",
/// };
/// spec.Labels[WslccLabels.Project] = "myproject";
/// spec.Labels[WslccLabels.Service] = "web";
/// spec.Environment["NGINX_PORT"] = "80";
/// spec.Ports.Add("8080:80");
/// spec.Volumes.Add(new ServiceMount
/// {
///     Type = MountType.Volume,
///     Source = "myproject_data",
///     Target = "/usr/share/nginx/html",
///     ReadOnly = true,
/// });
///
/// var containerId = await provider.RunContainerAsync(spec, cancellationToken);
/// </code>
/// </example>
public sealed class ContainerRunSpec
{
    /// <summary>Image reference to create the container from; required.</summary>
    public string Image { get; set; } = string.Empty;

    /// <summary>Container name to assign, usually <see cref="WslccLabels.ContainerName"/>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Labels applied at creation time. The engine sets the <see cref="WslccLabels"/> keys so the
    /// container can later be found by project and service.
    /// </summary>
    public IDictionary<string, string> Labels { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// OCI annotations passed through to the runtime (<c>--annotation</c>). Distinct from
    /// <see cref="Labels"/>; WSLCC does not inject its own keys here.
    /// </summary>
    public IDictionary<string, string> Annotations { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Environment variables to set. A <c>null</c> value means "pass the name through" so the runtime
    /// inherits the value from the host.
    /// </summary>
    public IDictionary<string, string?> Environment { get; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>
    /// Absolute (or otherwise provider-readable) paths passed as <c>--env-file</c>. Applied before
    /// <see cref="Environment"/> so <c>environment:</c> keys win.
    /// </summary>
    public IList<string> EnvFiles { get; } = new List<string>();

    /// <summary>Raw port mappings in Compose form, e.g. "8080:80".</summary>
    public IList<string> Ports { get; } = new List<string>();

    /// <summary>
    /// Resolved mounts (named volumes project-prefixed, bind sources absolute). The CLI builder emits
    /// <c>-v</c>, <c>--mount</c>, or <c>--tmpfs</c> depending on <see cref="ServiceMount.Type"/> and options.
    /// </summary>
    public IList<ServiceMount> Volumes { get; } = new List<ServiceMount>();

    /// <summary>Network the container is attached to at creation time. Additional networks are connected afterward.</summary>
    public string? Network { get; set; }

    /// <summary>Network alias to publish on <see cref="Network"/> (usually the service name).</summary>
    public string? NetworkAlias { get; set; }

    /// <summary>Additional aliases on the initial network.</summary>
    public IList<string> NetworkAliases { get; } = new List<string>();

    /// <summary>Static IPv4 address requested on the initial network.</summary>
    public string? NetworkIPv4Address { get; set; }

    /// <summary>
    /// Entrypoint override (exec argv). The first token becomes <c>--entrypoint</c>; any further
    /// tokens are placed after the image (before <see cref="Command"/>), matching <c>docker run</c>.
    /// </summary>
    public IList<string> Entrypoint { get; } = new List<string>();

    /// <summary>Argv passed to the image's entrypoint (the container's <c>CMD</c>).</summary>
    public IList<string> Command { get; } = new List<string>();

    /// <summary>Passed as <c>-u</c>/<c>--user</c> when set.</summary>
    public string? User { get; set; }

    /// <summary>Passed as <c>-w</c>/<c>--workdir</c> when set.</summary>
    public string? WorkingDir { get; set; }

    /// <summary>Passed as <c>--hostname</c> when set.</summary>
    public string? Hostname { get; set; }

    public string? DomainName { get; set; }

    public string? Gpus { get; set; }

    public string? Cpus { get; set; }

    public string? MemoryLimit { get; set; }

    public IList<string> Dns { get; } = new List<string>();

    public IList<string> DnsOptions { get; } = new List<string>();

    public IList<string> DnsSearch { get; } = new List<string>();

    public string? ShmSize { get; set; }

    public bool StdinOpen { get; set; }

    public bool Tty { get; set; }

    public IDictionary<string, string> Ulimits { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public string? StopSignal { get; set; }

    public string? StopGracePeriod { get; set; }

    /// <summary>When <c>true</c>, passed as <c>--read-only</c> so the container root filesystem is read-only.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Restart policy passed as <c>--restart</c> when set (e.g. <c>always</c>, <c>unless-stopped</c>).</summary>
    public string? Restart { get; set; }

    /// <summary>Optional healthcheck to apply to the container (from the service's <c>healthcheck:</c>).</summary>
    public ContainerHealthCheck? HealthCheck { get; set; }

    /// <summary>Run detached (default true for compose up).</summary>
    public bool Detach { get; set; } = true;
}

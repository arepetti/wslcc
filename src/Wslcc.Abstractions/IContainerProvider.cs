namespace Wslcc.Abstractions;

/// <summary>
/// A backend capable of managing images and containers (e.g. WSL containers or Docker).
/// The compose engine drives providers at the container level, so the same orchestration works for
/// every provider whose CLI/API follows the standard container tooling model.
/// </summary>
public interface IContainerProvider
{
    /// <summary>Stable identifier used on the command line (e.g. <c>wslc</c>, <c>docker</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Returns provider metadata including whether the underlying tooling is available and its version.
    /// Implementations must not throw when the tooling is missing; they should return
    /// <see cref="ProviderInfo.IsAvailable"/> = <c>false</c> instead.
    /// </summary>
    Task<ProviderInfo> GetProviderInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures an image is present locally, pulling it if missing (or always when
    /// <paramref name="alwaysPull"/> is set).
    /// </summary>
    Task EnsureImageAsync(string image, bool alwaysPull, CancellationToken cancellationToken = default);

    /// <summary>Returns whether an image is already present locally (no pulling).</summary>
    Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken = default);

    /// <summary>Builds an image from a Dockerfile, tagging it as <see cref="ImageBuildSpec.Tag"/>.</summary>
    Task BuildImageAsync(ImageBuildSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Creates and starts a container, returning its id.</summary>
    Task<string> RunContainerAsync(ContainerRunSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Creates a project network if it does not already exist (no-op when present).</summary>
    Task EnsureNetworkAsync(NetworkCreateSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Creates a named volume if it does not already exist (no-op when present).</summary>
    Task EnsureVolumeAsync(VolumeCreateSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Connects an already-running container to an additional network, optionally with an alias.</summary>
    Task ConnectNetworkAsync(string network, string container, string? alias, CancellationToken cancellationToken = default);

    /// <summary>Names of the networks labelled for the project (those wslcc created for it).</summary>
    Task<IReadOnlyList<string>> ListNetworkNamesAsync(string projectName, CancellationToken cancellationToken = default);

    /// <summary>Names of the volumes labelled for the project (those wslcc created for it).</summary>
    Task<IReadOnlyList<string>> ListVolumeNamesAsync(string projectName, CancellationToken cancellationToken = default);

    /// <summary>Removes a network (no-op-safe: callers treat failures as best-effort during teardown).</summary>
    Task RemoveNetworkAsync(string network, CancellationToken cancellationToken = default);

    /// <summary>Removes a named volume.</summary>
    Task RemoveVolumeAsync(string volume, CancellationToken cancellationToken = default);

    /// <summary>Stops a running container (no-op if already stopped).</summary>
    Task StopContainerAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>Starts a previously-created, stopped container (no-op if already running).</summary>
    Task StartContainerAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>Restarts a container, whether running or stopped.</summary>
    Task RestartContainerAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>Removes a container.</summary>
    Task RemoveContainerAsync(string container, bool force, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current runtime state (status, health, exit code) of a container, or <c>null</c> when
    /// it does not exist. Used to evaluate <c>depends_on</c> conditions (<c>service_healthy</c>,
    /// <c>service_completed_successfully</c>).
    /// </summary>
    Task<ContainerRuntimeState?> GetContainerStateAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists containers managed by WSLCC. When <paramref name="projectName"/> is provided, only that
    /// project's containers are returned.
    /// </summary>
    Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(
        string? projectName,
        bool all,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams a container's log lines. When <paramref name="follow"/> is <c>true</c>, the stream stays
    /// open and yields new lines as they are written until <paramref name="cancellationToken"/> is
    /// cancelled or the container stops. When <paramref name="tail"/> is set, only the last N lines are
    /// included in the initial output. When <paramref name="timestamps"/> is <c>true</c>, each returned
    /// line carries its parsed <see cref="ContainerLogLine.Timestamp"/>. <paramref name="since"/> filters
    /// to lines newer than a duration (e.g. <c>10m</c>) or an RFC3339 timestamp; <c>null</c>/empty means
    /// "no lower bound".
    /// </summary>
    IAsyncEnumerable<ContainerLogLine> GetLogsAsync(
        string container,
        bool follow,
        int? tail,
        bool timestamps,
        string? since,
        CancellationToken cancellationToken = default);
}

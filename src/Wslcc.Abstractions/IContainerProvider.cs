namespace Wslcc.Abstractions;

/// <summary>
/// A backend capable of managing images and containers (e.g. WSL containers or Docker).
/// The compose engine drives providers at the container level, so the same orchestration works for
/// every provider whose CLI/API follows the standard container tooling model.
/// </summary>
/// <remarks>
/// Implementations receive already-resolved inputs (absolute paths, project-prefixed network and volume
/// names) and should not apply Compose semantics of their own. Apart from
/// <see cref="GetProviderInfoAsync"/>, which reports unavailable tooling instead of throwing, a failed
/// operation raises <see cref="ProviderException"/>.
/// </remarks>
/// <example>
/// A minimal provider is enough to bring a container up; see <c>CliContainerProviderBase</c> for a base
/// class that implements this interface over a standard container CLI.
/// <code>
/// IContainerProvider provider = new DockerContainerProvider();
///
/// var info = await provider.GetProviderInfoAsync(cancellationToken);
/// if (!info.IsAvailable)
/// {
///     throw new InvalidOperationException(info.Details);
/// }
///
/// await provider.EnsureImageAsync("nginx:alpine", alwaysPull: false, cancellationToken);
/// await provider.EnsureNetworkAsync(
///     new NetworkCreateSpec { Name = WslccLabels.DefaultNetworkName("myproject") }, cancellationToken);
///
/// var containerId = await provider.RunContainerAsync(spec, cancellationToken);
/// </code>
/// </example>
public interface IContainerProvider
{
    /// <summary>Stable identifier used on the command line (e.g. <c>wslc</c>, <c>docker</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Returns provider metadata including whether the underlying tooling is available and its version.
    /// Implementations must not throw when the tooling is missing; they should return
    /// <see cref="ProviderInfo.IsAvailable"/> = <c>false</c> instead.
    /// </summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The provider's metadata, whether or not its tooling was found.</returns>
    Task<ProviderInfo> GetProviderInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures an image is present locally, pulling it if missing (or always when
    /// <paramref name="alwaysPull"/> is set).
    /// </summary>
    /// <param name="image">Image reference to make available.</param>
    /// <param name="alwaysPull">Pull even when the image is already present locally.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ArgumentException"><paramref name="image"/> is null, empty or whitespace.</exception>
    /// <exception cref="ProviderException">The pull failed.</exception>
    Task EnsureImageAsync(string image, bool alwaysPull, CancellationToken cancellationToken = default);

    /// <summary>Returns whether an image is already present locally (no pulling).</summary>
    /// <param name="image">Image reference to look for.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><c>true</c> when the image is present locally.</returns>
    /// <exception cref="ArgumentException"><paramref name="image"/> is null, empty or whitespace.</exception>
    Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken = default);

    /// <summary>Builds an image from a Dockerfile, tagging it as <see cref="ImageBuildSpec.Tag"/>.</summary>
    /// <param name="spec">The build context, Dockerfile, build args and target tag.</param>
    /// <param name="cancellationToken">Cancels the build.</param>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <c>null</c>.</exception>
    /// <exception cref="ProviderException">No build context was given, or the build failed.</exception>
    Task BuildImageAsync(ImageBuildSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Creates and starts a container, returning its id.</summary>
    /// <param name="spec">The container to create; all paths and names are already resolved.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The new container's id.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <c>null</c>.</exception>
    /// <exception cref="ProviderException">The spec has no image, or the container could not be started.</exception>
    Task<string> RunContainerAsync(ContainerRunSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Creates a project network if it does not already exist (no-op when present).</summary>
    /// <param name="spec">The network to ensure, with its project labels.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The spec has no name.</exception>
    /// <exception cref="ProviderException">The network was missing and could not be created.</exception>
    Task EnsureNetworkAsync(NetworkCreateSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Creates a named volume if it does not already exist (no-op when present).</summary>
    /// <param name="spec">The volume to ensure, with its project labels.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The spec has no name.</exception>
    /// <exception cref="ProviderException">The volume was missing and could not be created.</exception>
    Task EnsureVolumeAsync(VolumeCreateSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Connects an already-running container to an additional network, optionally with an alias.</summary>
    /// <param name="network">Network to connect to.</param>
    /// <param name="container">Container to connect.</param>
    /// <param name="alias">Alias to publish on the network, usually the service name; <c>null</c> for none.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ArgumentException"><paramref name="network"/> or <paramref name="container"/> is null, empty or whitespace.</exception>
    /// <exception cref="ProviderException">The container could not be connected.</exception>
    Task ConnectNetworkAsync(string network, string container, string? alias, CancellationToken cancellationToken = default);

    /// <summary>Names of the networks labelled for the project (those wslcc created for it).</summary>
    /// <param name="projectName">Project to scope to.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The project's network names; empty when it has none.</returns>
    /// <exception cref="ArgumentException"><paramref name="projectName"/> is null, empty or whitespace.</exception>
    /// <exception cref="ProviderException">The networks could not be listed.</exception>
    Task<IReadOnlyList<string>> ListNetworkNamesAsync(string projectName, CancellationToken cancellationToken = default);

    /// <summary>Names of the volumes labelled for the project (those wslcc created for it).</summary>
    /// <param name="projectName">Project to scope to.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The project's volume names; empty when it has none.</returns>
    /// <exception cref="ArgumentException"><paramref name="projectName"/> is null, empty or whitespace.</exception>
    /// <exception cref="ProviderException">The volumes could not be listed.</exception>
    Task<IReadOnlyList<string>> ListVolumeNamesAsync(string projectName, CancellationToken cancellationToken = default);

    /// <summary>Removes a network (no-op-safe: callers treat failures as best-effort during teardown).</summary>
    /// <param name="network">Network to remove.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ArgumentException"><paramref name="network"/> is null, empty or whitespace.</exception>
    /// <exception cref="ProviderException">The network could not be removed.</exception>
    Task RemoveNetworkAsync(string network, CancellationToken cancellationToken = default);

    /// <summary>Removes a named volume.</summary>
    /// <param name="volume">Volume to remove.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ArgumentException"><paramref name="volume"/> is null, empty or whitespace.</exception>
    /// <exception cref="ProviderException">The volume could not be removed.</exception>
    Task RemoveVolumeAsync(string volume, CancellationToken cancellationToken = default);

    /// <summary>Stops a running container (no-op if already stopped).</summary>
    /// <param name="container">Container id or name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ArgumentException"><paramref name="container"/> is null, empty or whitespace.</exception>
    /// <exception cref="ProviderException">The container could not be stopped.</exception>
    Task StopContainerAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>Starts a previously-created, stopped container (no-op if already running).</summary>
    /// <param name="container">Container id or name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ArgumentException"><paramref name="container"/> is null, empty or whitespace.</exception>
    /// <exception cref="ProviderException">The container could not be started.</exception>
    Task StartContainerAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>Restarts a container, whether running or stopped.</summary>
    /// <param name="container">Container id or name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ArgumentException"><paramref name="container"/> is null, empty or whitespace.</exception>
    /// <exception cref="ProviderException">The container could not be restarted.</exception>
    Task RestartContainerAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>Removes a container.</summary>
    /// <param name="container">Container id or name.</param>
    /// <param name="force">Remove even when the container is still running.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ArgumentException"><paramref name="container"/> is null, empty or whitespace.</exception>
    /// <exception cref="ProviderException">The container could not be removed.</exception>
    Task RemoveContainerAsync(string container, bool force, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current runtime state (status, health, exit code) of a container, or <c>null</c> when
    /// it does not exist. Used to evaluate <c>depends_on</c> conditions (<c>service_healthy</c>,
    /// <c>service_completed_successfully</c>).
    /// </summary>
    /// <param name="container">Container id or name.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The container's state, or <c>null</c> when it does not exist.</returns>
    Task<ContainerRuntimeState?> GetContainerStateAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists containers managed by WSLCC. When <paramref name="projectName"/> is provided, only that
    /// project's containers are returned.
    /// </summary>
    /// <param name="projectName">Project to scope to, or <c>null</c> for every wslcc-managed container.</param>
    /// <param name="all">Include stopped containers, not just running ones.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching containers.</returns>
    /// <exception cref="ProviderException">The containers could not be listed.</exception>
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
    /// <param name="container">Container id or name.</param>
    /// <param name="follow">Keep the stream open and yield new lines as they are written.</param>
    /// <param name="tail">Include only the last N lines of existing output; <c>null</c> means all of it.</param>
    /// <param name="timestamps">Request timestamps, populating <see cref="ContainerLogLine.Timestamp"/>.</param>
    /// <param name="since">Lower time bound: a duration (e.g. <c>10m</c>) or an RFC3339 timestamp.</param>
    /// <param name="cancellationToken">Ends the stream; required to stop a <paramref name="follow"/> read.</param>
    /// <returns>The container's log lines, in the order they were written.</returns>
    IAsyncEnumerable<ContainerLogLine> GetLogsAsync(
        string container,
        bool follow,
        int? tail,
        bool timestamps,
        string? since,
        CancellationToken cancellationToken = default);
}

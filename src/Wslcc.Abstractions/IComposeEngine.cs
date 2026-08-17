using Wslcc.Abstractions.Compose;

namespace Wslcc.Abstractions;

/// <summary>
/// The provider-agnostic orchestration surface. Drives a selected provider to bring a Compose project
/// up/down and to list its containers.
/// </summary>
/// <remarks>
/// Every operation takes an optional <c>providerName</c>; passing <c>null</c> or an empty string uses
/// the default provider. Operations that act on many services never throw because one of them failed —
/// the per-service outcome is captured in the returned <see cref="ServiceOperationResult"/> list — so
/// callers should inspect <see cref="ServiceOperationResult.Failed"/> rather than rely on exceptions.
/// </remarks>
/// <example>
/// Bringing a project up and reporting what happened:
/// <code>
/// IComposeEngine engine = new ComposeEngine(providers);
/// ComposeFile file = new ComposeFileParser().Parse(resolvedYaml);
///
/// var progress = new Progress&lt;ServiceProgressUpdate&gt;(
///     u => Console.WriteLine($"{u.Service}: {u.Phase} {u.Status}"));
///
/// var results = await engine.UpAsync(
///     projectName: "myproject",
///     file: file,
///     providerName: null,
///     pull: false,
///     buildPolicy: BuildPolicy.Auto,
///     baseDirectory: projectDirectory,
///     serviceConfigHashes: hashes,
///     progress: progress,
///     cancellationToken: cancellationToken);
///
/// foreach (var result in results.Where(r => r.Failed))
/// {
///     Console.Error.WriteLine($"{result.Service}: {result.Error}");
/// }
/// </code>
/// </example>
public interface IComposeEngine
{
    /// <summary>Names of all registered providers.</summary>
    IReadOnlyList<string> ProviderNames { get; }

    /// <summary>Returns info for every registered provider.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>One entry per registered provider, in registration order.</returns>
    Task<IReadOnlyList<ProviderInfo>> GetProviderInfosAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns info for a single provider. When <paramref name="providerName"/> is null or empty,
    /// the default provider is used.
    /// </summary>
    /// <param name="providerName">Provider to query, or <c>null</c>/empty for the default one.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The provider's metadata, including whether its tooling is available.</returns>
    /// <exception cref="ProviderException">No provider is registered under that name.</exception>
    Task<ProviderInfo> GetProviderInfoAsync(string? providerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and starts containers for every service, in dependency order. A service that declares a
    /// <c>build:</c> section is built according to <paramref name="buildPolicy"/> (relative build contexts
    /// resolve against <paramref name="baseDirectory"/>). Before a service is started, its
    /// <c>depends_on</c> conditions are honored: <c>service_healthy</c> waits for a healthy healthcheck
    /// and <c>service_completed_successfully</c> waits for a clean exit. A service whose required
    /// dependency fails (or is unhealthy / exits non-zero) is not started.
    /// <para>
    /// Change detection: when <paramref name="serviceConfigHashes"/> maps a service to its resolved
    /// config hash, an existing container that is still running and carries the same hash is left in
    /// place (reported as <c>running</c>) instead of being recreated. Otherwise the existing container is
    /// replaced. Passing <c>--pull</c> or <c>--build</c> (<see cref="BuildPolicy.Always"/>) forces
    /// recreation regardless of the hash.
    /// </para>
    /// When <paramref name="progress"/> is provided, per-service updates are reported as work proceeds.
    /// Never throws for a single service failure; the outcome is captured per service.
    /// </summary>
    /// <param name="projectName">Project the containers are labelled with and named after.</param>
    /// <param name="file">The resolved Compose file describing the services to start.</param>
    /// <param name="providerName">Provider to drive, or <c>null</c>/empty for the default one.</param>
    /// <param name="pull">Always pull each service's image, even when it is already present locally.</param>
    /// <param name="buildPolicy">Whether services with a <c>build:</c> section are (re)built.</param>
    /// <param name="baseDirectory">Directory relative build contexts and bind sources resolve against.</param>
    /// <param name="serviceConfigHashes">Resolved config hash per service, enabling change detection.</param>
    /// <param name="progress">Receives per-service updates while the operation runs.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>One outcome per service the engine acted on, in start order.</returns>
    /// <exception cref="ArgumentException"><paramref name="projectName"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is <c>null</c>.</exception>
    Task<IReadOnlyList<ServiceOperationResult>> UpAsync(
        string projectName,
        ComposeFile file,
        string? providerName,
        bool pull,
        BuildPolicy buildPolicy,
        string? baseDirectory,
        IReadOnlyDictionary<string, string>? serviceConfigHashes = null,
        IProgress<ServiceProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops and removes all containers belonging to the project, in reverse <c>depends_on</c> order
    /// (dependents first) when <paramref name="file"/> is provided. Project networks created by wslcc are
    /// then removed; named project volumes are removed only when <paramref name="removeVolumes"/> is set
    /// (matching <c>docker compose down --volumes</c>, which preserves data by default).
    /// </summary>
    /// <param name="projectName">Project whose containers are torn down.</param>
    /// <param name="file">The Compose file, used for teardown ordering; <c>null</c> tears down in no particular order.</param>
    /// <param name="providerName">Provider to drive, or <c>null</c>/empty for the default one.</param>
    /// <param name="removeVolumes">Also remove the project's named volumes (data is otherwise preserved).</param>
    /// <param name="progress">Receives per-service updates while the operation runs.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>One outcome per container removed, plus entries for the networks and volumes cleaned up.</returns>
    /// <exception cref="ArgumentException"><paramref name="projectName"/> is null, empty or whitespace.</exception>
    Task<IReadOnlyList<ServiceOperationResult>> DownAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        bool removeVolumes = false,
        IProgress<ServiceProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists containers belonging to the project, or every wslcc-managed container when
    /// <paramref name="projectName"/> is <c>null</c>.
    /// </summary>
    /// <param name="projectName">Project to scope to, or <c>null</c> for every wslcc-managed container.</param>
    /// <param name="providerName">Provider to query, or <c>null</c>/empty for the default one.</param>
    /// <param name="all">Include stopped containers, not just running ones.</param>
    /// <param name="progress">Receives updates while the operation runs.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching containers as the provider reports them.</returns>
    Task<IReadOnlyList<ContainerInfo>> PsAsync(
        string? projectName,
        string? providerName,
        bool all,
        IProgress<ServiceProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts existing (stopped) containers for the project, in <c>depends_on</c> order (dependencies
    /// first) when <paramref name="file"/> is provided. When <paramref name="services"/> is null or
    /// empty, every existing container for the project is started; a requested service name that the
    /// project does not define is rejected. Never throws for a single container failure; the outcome is
    /// captured per service.
    /// </summary>
    /// <param name="projectName">Project whose containers are started.</param>
    /// <param name="file">The Compose file, used for start ordering and service-name validation.</param>
    /// <param name="providerName">Provider to drive, or <c>null</c>/empty for the default one.</param>
    /// <param name="services">Services to start; <c>null</c>/empty means all of the project's containers.</param>
    /// <param name="progress">Receives per-service updates while the operation runs.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>One outcome per service the engine acted on, in start order.</returns>
    /// <exception cref="ArgumentException"><paramref name="projectName"/> is null, empty or whitespace.</exception>
    /// <exception cref="ProviderException">A requested service is not defined by the project.</exception>
    Task<IReadOnlyList<ServiceOperationResult>> StartAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        IProgress<ServiceProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the project's containers (without removing them) in reverse <c>depends_on</c> order
    /// (dependents first) when <paramref name="file"/> is provided. When <paramref name="services"/> is
    /// null or empty, every existing container for the project is stopped; a requested service name that
    /// the project does not define is rejected.
    /// </summary>
    /// <param name="projectName">Project whose containers are stopped.</param>
    /// <param name="file">The Compose file, used for stop ordering and service-name validation.</param>
    /// <param name="providerName">Provider to drive, or <c>null</c>/empty for the default one.</param>
    /// <param name="services">Services to stop; <c>null</c>/empty means all of the project's containers.</param>
    /// <param name="progress">Receives per-service updates while the operation runs.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>One outcome per service the engine acted on, in stop order.</returns>
    /// <exception cref="ArgumentException"><paramref name="projectName"/> is null, empty or whitespace.</exception>
    /// <exception cref="ProviderException">A requested service is not defined by the project.</exception>
    Task<IReadOnlyList<ServiceOperationResult>> StopAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        IProgress<ServiceProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restarts the project's containers, in <c>depends_on</c> order (dependencies first) when
    /// <paramref name="file"/> is provided. When <paramref name="services"/> is null or empty, every
    /// existing container for the project is restarted; a requested service name that the project does
    /// not define is rejected.
    /// </summary>
    /// <param name="projectName">Project whose containers are restarted.</param>
    /// <param name="file">The Compose file, used for restart ordering and service-name validation.</param>
    /// <param name="providerName">Provider to drive, or <c>null</c>/empty for the default one.</param>
    /// <param name="services">Services to restart; <c>null</c>/empty means all of the project's containers.</param>
    /// <param name="progress">Receives per-service updates while the operation runs.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>One outcome per service the engine acted on, in restart order.</returns>
    /// <exception cref="ArgumentException"><paramref name="projectName"/> is null, empty or whitespace.</exception>
    /// <exception cref="ProviderException">A requested service is not defined by the project.</exception>
    Task<IReadOnlyList<ServiceOperationResult>> RestartAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        IProgress<ServiceProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls the image for each service defined in <paramref name="file"/> (always, regardless of
    /// whether it is already present locally). Services with no <c>image:</c> (build-only) are
    /// skipped. When <paramref name="services"/> is null or empty, every service with an image is
    /// pulled. Never throws for a single service failure; the outcome is captured per service.
    /// </summary>
    /// <param name="file">The resolved Compose file naming the images to pull.</param>
    /// <param name="providerName">Provider to drive, or <c>null</c>/empty for the default one.</param>
    /// <param name="services">Services to pull; <c>null</c>/empty means every service with an image.</param>
    /// <param name="progress">Receives per-service updates while the operation runs.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>One outcome per service, with build-only services reported as skipped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is <c>null</c>.</exception>
    Task<IReadOnlyList<ServiceOperationResult>> PullAsync(
        ComposeFile file,
        string? providerName,
        IReadOnlyList<string>? services,
        IProgress<ServiceProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the image for each service with a <c>build:</c> section. Services with no <c>build:</c>
    /// are skipped (not reported as failures). Relative build contexts are resolved against
    /// <paramref name="baseDirectory"/> (the compose file's directory); when it is null/empty, relative
    /// contexts are passed through as-is. When <paramref name="services"/> is null or empty, every
    /// buildable service is built.
    /// </summary>
    /// <param name="projectName">Project the built images are tagged for.</param>
    /// <param name="file">The resolved Compose file describing the services to build.</param>
    /// <param name="providerName">Provider to drive, or <c>null</c>/empty for the default one.</param>
    /// <param name="baseDirectory">Directory relative build contexts resolve against.</param>
    /// <param name="services">Services to build; <c>null</c>/empty means every buildable service.</param>
    /// <param name="progress">Receives per-service updates while the operation runs.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>One outcome per service, with image-only services reported as skipped.</returns>
    /// <exception cref="ArgumentException"><paramref name="projectName"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is <c>null</c>.</exception>
    Task<IReadOnlyList<ServiceOperationResult>> BuildAsync(
        string projectName,
        ComposeFile file,
        string? providerName,
        string? baseDirectory,
        IReadOnlyList<string>? services,
        IProgress<ServiceProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams log lines from the project's existing containers, tagging each line with its service
    /// name. When <paramref name="services"/> is null/empty, every existing container for the project
    /// is included; a requested service name that the project does not define is rejected. Containers
    /// are attached in <c>depends_on</c> order (dependencies first) when <paramref name="file"/> is
    /// provided. When <paramref name="follow"/> is <c>true</c>, the stream stays open until
    /// <paramref name="cancellationToken"/> is cancelled and lines are interleaved as they arrive; when
    /// <c>false</c>, the bounded output is buffered and merged in <paramref name="timestamps"/> order.
    /// <paramref name="timestamps"/> also carries each line's time through to the caller; and
    /// <paramref name="since"/> filters to lines newer than a duration or RFC3339 timestamp.
    /// </summary>
    /// <param name="projectName">Project whose containers are read.</param>
    /// <param name="file">The Compose file, used for attach ordering and service-name validation.</param>
    /// <param name="providerName">Provider to drive, or <c>null</c>/empty for the default one.</param>
    /// <param name="services">Services to include; <c>null</c>/empty means all of the project's containers.</param>
    /// <param name="follow">Keep the stream open and yield new lines as they are written.</param>
    /// <param name="tail">Include only the last N lines of existing output; <c>null</c> means all of it.</param>
    /// <param name="timestamps">Request timestamps, which also enables timestamp-ordered merging.</param>
    /// <param name="since">Lower time bound: a duration (e.g. <c>10m</c>) or an RFC3339 timestamp.</param>
    /// <param name="cancellationToken">Ends the stream; required to stop a <paramref name="follow"/> read.</param>
    /// <returns>The merged log lines, each tagged with the service it came from.</returns>
    /// <exception cref="ArgumentException"><paramref name="projectName"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tail"/> is set and negative.</exception>
    /// <exception cref="ProviderException">A requested service is not defined by the project.</exception>
    IAsyncEnumerable<ServiceLogLine> GetLogsAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        bool follow,
        int? tail,
        bool timestamps,
        string? since,
        CancellationToken cancellationToken = default);
}

using Wslcc.Abstractions.Compose;

namespace Wslcc.Abstractions;

/// <summary>
/// The provider-agnostic orchestration surface. Drives a selected provider to bring a Compose project
/// up/down and to list its containers.
/// </summary>
public interface IComposeEngine
{
    /// <summary>Names of all registered providers.</summary>
    IReadOnlyList<string> ProviderNames { get; }

    /// <summary>Returns info for every registered provider.</summary>
    Task<IReadOnlyList<ProviderInfo>> GetProviderInfosAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns info for a single provider. When <paramref name="providerName"/> is null or empty,
    /// the default provider is used.
    /// </summary>
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
    /// Never throws for a single service failure; the outcome is captured per service.
    /// </summary>
    Task<IReadOnlyList<ServiceOperationResult>> UpAsync(
        string projectName,
        ComposeFile file,
        string? providerName,
        bool pull,
        BuildPolicy buildPolicy,
        string? baseDirectory,
        IReadOnlyDictionary<string, string>? serviceConfigHashes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops and removes all containers belonging to the project, in reverse <c>depends_on</c> order
    /// (dependents first) when <paramref name="file"/> is provided.
    /// </summary>
    Task<IReadOnlyList<ServiceOperationResult>> DownAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists containers belonging to the project, or every wslcc-managed container when
    /// <paramref name="projectName"/> is <c>null</c>.
    /// </summary>
    Task<IReadOnlyList<ContainerInfo>> PsAsync(
        string? projectName,
        string? providerName,
        bool all,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts existing (stopped) containers for the project, in <c>depends_on</c> order (dependencies
    /// first) when <paramref name="file"/> is provided. When <paramref name="services"/> is null or
    /// empty, every existing container for the project is started; a requested service name that the
    /// project does not define is rejected. Never throws for a single container failure; the outcome is
    /// captured per service.
    /// </summary>
    Task<IReadOnlyList<ServiceOperationResult>> StartAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the project's containers (without removing them) in reverse <c>depends_on</c> order
    /// (dependents first) when <paramref name="file"/> is provided. When <paramref name="services"/> is
    /// null or empty, every existing container for the project is stopped; a requested service name that
    /// the project does not define is rejected.
    /// </summary>
    Task<IReadOnlyList<ServiceOperationResult>> StopAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restarts the project's containers, in <c>depends_on</c> order (dependencies first) when
    /// <paramref name="file"/> is provided. When <paramref name="services"/> is null or empty, every
    /// existing container for the project is restarted; a requested service name that the project does
    /// not define is rejected.
    /// </summary>
    Task<IReadOnlyList<ServiceOperationResult>> RestartAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls the image for each service defined in <paramref name="file"/> (always, regardless of
    /// whether it is already present locally). When <paramref name="services"/> is null or empty,
    /// every service with an image is pulled. Never throws for a single service failure; the outcome
    /// is captured per service.
    /// </summary>
    Task<IReadOnlyList<ServiceOperationResult>> PullAsync(
        ComposeFile file,
        string? providerName,
        IReadOnlyList<string>? services,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the image for each service with a <c>build:</c> section. Services with no <c>build:</c>
    /// are skipped (not reported as failures). Relative build contexts are resolved against
    /// <paramref name="baseDirectory"/> (the compose file's directory); when it is null/empty, relative
    /// contexts are passed through as-is. When <paramref name="services"/> is null or empty, every
    /// buildable service is built.
    /// </summary>
    Task<IReadOnlyList<ServiceOperationResult>> BuildAsync(
        string projectName,
        ComposeFile file,
        string? providerName,
        string? baseDirectory,
        IReadOnlyList<string>? services,
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

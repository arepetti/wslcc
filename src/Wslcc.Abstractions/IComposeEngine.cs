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
    /// Creates and starts containers for every service, in dependency order. Existing containers with
    /// the same name are recreated. Never throws for a single service failure; the outcome is captured
    /// per service.
    /// </summary>
    Task<IReadOnlyList<ServiceOperationResult>> UpAsync(
        string projectName,
        ComposeFile file,
        string? providerName,
        bool pull,
        CancellationToken cancellationToken = default);

    /// <summary>Stops and removes all containers belonging to the project.</summary>
    Task<IReadOnlyList<ServiceOperationResult>> DownAsync(
        string projectName,
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
    /// Starts existing (stopped) containers for the project. When <paramref name="services"/> is null
    /// or empty, every existing container for the project is started. Never throws for a single
    /// container failure; the outcome is captured per service.
    /// </summary>
    Task<IReadOnlyList<ServiceOperationResult>> StartAsync(
        string projectName,
        string? providerName,
        IReadOnlyList<string>? services,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the project's containers without removing them. When <paramref name="services"/> is null
    /// or empty, every existing container for the project is stopped.
    /// </summary>
    Task<IReadOnlyList<ServiceOperationResult>> StopAsync(
        string projectName,
        string? providerName,
        IReadOnlyList<string>? services,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restarts the project's containers. When <paramref name="services"/> is null or empty, every
    /// existing container for the project is restarted.
    /// </summary>
    Task<IReadOnlyList<ServiceOperationResult>> RestartAsync(
        string projectName,
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
    /// is included. When <paramref name="follow"/> is <c>true</c>, the stream stays open until
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    IAsyncEnumerable<ServiceLogLine> GetLogsAsync(
        string projectName,
        string? providerName,
        IReadOnlyList<string>? services,
        bool follow,
        int? tail,
        CancellationToken cancellationToken = default);
}

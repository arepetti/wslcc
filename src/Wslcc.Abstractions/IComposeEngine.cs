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
}

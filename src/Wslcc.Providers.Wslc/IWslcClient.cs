using Wslcc.Abstractions;

namespace Wslcc.Providers.Wslc;

/// <summary>
/// Testable seam over the operations exposed by the <c>Microsoft.WSL.Containers</c> managed API.
/// Operations missing from that API are deliberately kept in the provider's session-scoped CLI
/// fallback rather than being hidden behind this contract.
/// </summary>
public interface IWslcClient : IDisposable
{
    Task<ProviderInfo> GetProviderInfoAsync(CancellationToken cancellationToken = default);

    Task EnsureSessionAsync(CancellationToken cancellationToken = default);

    Task EnsureImageAsync(string image, bool alwaysPull, CancellationToken cancellationToken = default);

    Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken = default);

    Task StartContainerAsync(string container, CancellationToken cancellationToken = default);

    Task StopContainerAsync(string container, CancellationToken cancellationToken = default);

    Task RemoveContainerAsync(string container, bool force, CancellationToken cancellationToken = default);

    Task<ContainerRuntimeState?> GetContainerStateAsync(string container, CancellationToken cancellationToken = default);
}

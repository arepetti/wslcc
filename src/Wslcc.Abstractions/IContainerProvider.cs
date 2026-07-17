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

    /// <summary>Creates and starts a container, returning its id.</summary>
    Task<string> RunContainerAsync(ContainerRunSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Stops a running container (no-op if already stopped).</summary>
    Task StopContainerAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>Removes a container.</summary>
    Task RemoveContainerAsync(string container, bool force, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists containers managed by WSLCC. When <paramref name="projectName"/> is provided, only that
    /// project's containers are returned.
    /// </summary>
    Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(
        string? projectName,
        bool all,
        CancellationToken cancellationToken = default);
}

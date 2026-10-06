using Wslcc.Abstractions;

namespace Wslcc.Providers.Wslc;

/// <summary>
/// Hybrid provider for Microsoft's WSL containers feature. It prefers the managed API and uses the
/// GA CLI, scoped to the same named session, only for capabilities absent from that API.
/// </summary>
public sealed class WslcProvider : IContainerProvider, IDisposable
{
    public const string ProviderName = "wslc";

    private readonly IWslcClient _client;
    private readonly WslcCliClient _cli;

    public WslcProvider()
        : this(new WslcSdkClient(), new WslcCliClient())
    {
    }

    public WslcProvider(IWslcClient client)
        : this(client, new WslcCliClient())
    {
    }

    internal WslcProvider(IWslcClient client, WslcCliClient cli)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _cli = cli ?? throw new ArgumentNullException(nameof(cli));
    }

    public string Name => ProviderName;

    public Task<ProviderInfo> GetProviderInfoAsync(CancellationToken cancellationToken = default)
        => _client.GetProviderInfoAsync(cancellationToken);

    public Task EnsureImageAsync(
        string image,
        bool alwaysPull,
        CancellationToken cancellationToken = default)
        => _client.EnsureImageAsync(image, alwaysPull, cancellationToken);

    public Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken = default)
        => _client.ImageExistsAsync(image, cancellationToken);

    public async Task BuildImageAsync(ImageBuildSpec spec, CancellationToken cancellationToken = default)
    {
        await _client.EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        await _cli.BuildImageAsync(spec, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> RunContainerAsync(
        ContainerRunSpec spec,
        CancellationToken cancellationToken = default)
    {
        await _client.EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        return await _cli.RunContainerAsync(spec, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnsureNetworkAsync(
        NetworkCreateSpec spec,
        CancellationToken cancellationToken = default)
    {
        await _client.EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        await _cli.EnsureNetworkAsync(spec, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnsureVolumeAsync(
        VolumeCreateSpec spec,
        CancellationToken cancellationToken = default)
    {
        await _client.EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        await _cli.EnsureVolumeAsync(spec, cancellationToken).ConfigureAwait(false);
    }

    public async Task ConnectNetworkAsync(
        string network,
        string container,
        IReadOnlyList<string>? aliases,
        string? ipv4Address,
        CancellationToken cancellationToken = default)
    {
        await _client.EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        await _cli.ConnectNetworkAsync(network, container, aliases, ipv4Address, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListNetworkNamesAsync(
        string projectName,
        CancellationToken cancellationToken = default)
    {
        await _client.EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        return await _cli.ListNetworkNamesAsync(projectName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListVolumeNamesAsync(
        string projectName,
        CancellationToken cancellationToken = default)
    {
        await _client.EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        return await _cli.ListVolumeNamesAsync(projectName, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveNetworkAsync(string network, CancellationToken cancellationToken = default)
    {
        await _client.EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        await _cli.RemoveNetworkAsync(network, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveVolumeAsync(string volume, CancellationToken cancellationToken = default)
    {
        await _client.EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        await _cli.RemoveVolumeAsync(volume, cancellationToken).ConfigureAwait(false);
    }

    public Task StopContainerAsync(string container, CancellationToken cancellationToken = default)
        => _client.StopContainerAsync(container, cancellationToken);

    public Task StartContainerAsync(string container, CancellationToken cancellationToken = default)
        => _client.StartContainerAsync(container, cancellationToken);

    public async Task RestartContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        var state = await _client.GetContainerStateAsync(container, cancellationToken).ConfigureAwait(false);
        if (state is null)
            throw new ProviderException($"Container '{container}' was not found.");

        if (!state.HasExited)
            await _client.StopContainerAsync(container, cancellationToken).ConfigureAwait(false);

        await _client.StartContainerAsync(container, cancellationToken).ConfigureAwait(false);
    }

    public Task RemoveContainerAsync(
        string container,
        bool force,
        CancellationToken cancellationToken = default)
        => _client.RemoveContainerAsync(container, force, cancellationToken);

    public Task<ContainerRuntimeState?> GetContainerStateAsync(
        string container,
        CancellationToken cancellationToken = default)
        => _client.GetContainerStateAsync(container, cancellationToken);

    public async Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(
        string? projectName,
        bool all,
        CancellationToken cancellationToken = default)
    {
        await _client.EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        return await _cli.ListContainersAsync(projectName, all, cancellationToken).ConfigureAwait(false);
    }

    public IAsyncEnumerable<ContainerLogLine> GetLogsAsync(
        string container,
        bool follow,
        int? tail,
        bool timestamps,
        string? since,
        CancellationToken cancellationToken = default)
        => GetLogsCoreAsync(container, follow, tail, timestamps, since, cancellationToken);

    public void Dispose() => _client.Dispose();

    private async IAsyncEnumerable<ContainerLogLine> GetLogsCoreAsync(
        string container,
        bool follow,
        int? tail,
        bool timestamps,
        string? since,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await _client.EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var line in _cli.GetLogsAsync(
                           container,
                           follow,
                           tail,
                           timestamps,
                           since,
                           cancellationToken)
                           .ConfigureAwait(false))
            yield return line;
    }
}

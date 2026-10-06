using Wslcc.Abstractions;
using Wslcc.Providers.Common;

namespace Wslcc.Providers.Wslc;

/// <summary>
/// Session-scoped fallback for capabilities not projected by <c>Microsoft.WSL.Containers</c>.
/// </summary>
internal sealed class WslcCliClient
{
    public async Task BuildImageAsync(ImageBuildSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var result = await RunAsync(WslcCommandBuilder.BuildBuildArguments(spec), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, $"build image '{spec.Tag}'");
    }

    public async Task<string> RunContainerAsync(ContainerRunSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var result = await RunAsync(WslcCommandBuilder.BuildRunArguments(spec), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, $"create container '{spec.Name}'");
        return LastNonEmptyLine(result!.StandardOutput);
    }

    public async Task EnsureNetworkAsync(NetworkCreateSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var inspect = await RunAsync(WslcCommandBuilder.BuildNetworkInspectArguments(spec.Name), cancellationToken)
            .ConfigureAwait(false);
        if (inspect is { Success: true })
            return;

        var create = await RunAsync(WslcCommandBuilder.BuildNetworkCreateArguments(spec), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(create, $"create network '{spec.Name}'");
    }

    public async Task EnsureVolumeAsync(VolumeCreateSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var inspect = await RunAsync(WslcCommandBuilder.BuildVolumeInspectArguments(spec.Name), cancellationToken)
            .ConfigureAwait(false);
        if (inspect is { Success: true })
            return;

        var create = await RunAsync(WslcCommandBuilder.BuildVolumeCreateArguments(spec), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(create, $"create volume '{spec.Name}'");
    }

    public async Task ConnectNetworkAsync(
        string network,
        string container,
        IReadOnlyList<string>? aliases,
        string? ipv4Address,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(
                WslcCommandBuilder.BuildNetworkConnectArguments(network, container, aliases, ipv4Address),
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result, $"connect container '{container}' to network '{network}'");
    }

    public async Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(
        string? projectName,
        bool all,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(WslcCommandBuilder.BuildPsArguments(projectName, all), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result, "list containers");
        return WslcJsonParser.ParseContainers(result!.StandardOutput);
    }

    public async Task<IReadOnlyList<string>> ListNetworkNamesAsync(
        string projectName,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(WslcCommandBuilder.BuildNetworkListArguments(projectName), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result, "list networks");
        return WslcJsonParser.ParseNames(result!.StandardOutput);
    }

    public async Task<IReadOnlyList<string>> ListVolumeNamesAsync(
        string projectName,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(WslcCommandBuilder.BuildVolumeListArguments(projectName), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result, "list volumes");
        return WslcJsonParser.ParseNames(result!.StandardOutput);
    }

    public async Task RemoveNetworkAsync(string network, CancellationToken cancellationToken)
    {
        var result = await RunAsync(WslcCommandBuilder.BuildNetworkRemoveArguments(network), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result, $"remove network '{network}'");
    }

    public async Task RemoveVolumeAsync(string volume, CancellationToken cancellationToken)
    {
        var result = await RunAsync(WslcCommandBuilder.BuildVolumeRemoveArguments(volume), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result, $"remove volume '{volume}'");
    }

    public IAsyncEnumerable<ContainerLogLine> GetLogsAsync(
        string container,
        bool follow,
        int? tail,
        bool timestamps,
        string? since,
        CancellationToken cancellationToken)
        => GetLogsCoreAsync(container, follow, tail, timestamps, since, cancellationToken);

    private async IAsyncEnumerable<ContainerLogLine> GetLogsCoreAsync(
        string container,
        bool follow,
        int? tail,
        bool timestamps,
        string? since,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var arguments = WslcCommandBuilder.BuildLogsArguments(container, follow, tail, timestamps, since);
        await foreach (var raw in ProcessRunner.StreamLinesAsync("wslc", arguments, cancellationToken).ConfigureAwait(false))
            yield return timestamps ? CliLogParser.ParseTimestamped(raw) : new ContainerLogLine(null, raw);
    }

    private static Task<ProcessResult?> RunAsync(string arguments, CancellationToken cancellationToken)
        => ProcessRunner.TryRunAsync("wslc", arguments, cancellationToken);

    private static void EnsureSuccess(ProcessResult? result, string action)
    {
        if (result is null)
            throw new ProviderException("The 'wslc' executable was not found on PATH. Run 'wsl --update'.");

        if (result.Success)
            return;

        var detail = result.StandardError.Trim();
        if (detail.Length == 0)
            detail = result.StandardOutput.Trim();

        throw new ProviderException($"Failed to {action} using 'wslc': {detail}");
    }

    private static string LastNonEmptyLine(string output)
    {
        var lines = output.Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length > 0)
                return line;
        }

        return string.Empty;
    }
}

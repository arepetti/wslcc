using Wslcc.Abstractions;

namespace Wslcc.Providers.Common;

/// <summary>
/// Base class for providers that drive a standard container CLI (docker, wslc). Container operations
/// are identical across those tools, so subclasses only supply the executable name, the provider name,
/// and how to report version/availability.
/// </summary>
/// <remarks>
/// Arguments come from <see cref="CliCommandBuilder"/> and output is read by
/// <see cref="CliStateParser"/> / <see cref="CliLogParser"/>, so a subclass never parses anything
/// itself. Any non-zero exit (or a missing executable) surfaces as a <see cref="ProviderException"/>
/// naming the action that failed.
/// </remarks>
public abstract class CliContainerProviderBase : IContainerProvider
{
    /// <summary>The executable to invoke (e.g. "docker", "wslc").</summary>
    protected abstract string Executable { get; }

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public abstract Task<ProviderInfo> GetProviderInfoAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public async Task EnsureImageAsync(string image, bool alwaysPull, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);

        var alreadyPresent = !alwaysPull && await ImageExistsAsync(image, cancellationToken).ConfigureAwait(false);
        if (alreadyPresent)
            return;

        var pull = await TryRunAsync(CliCommandBuilder.BuildPullArguments(image), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(pull, $"pull image '{image}'");
    }

    /// <inheritdoc/>
    public async Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);

        var inspect = await TryRunAsync(CliCommandBuilder.BuildImageInspectArguments(image), cancellationToken)
            .ConfigureAwait(false);
        return inspect is { Success: true };
    }

    /// <inheritdoc/>
    public async Task BuildImageAsync(ImageBuildSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var result = await TryRunAsync(CliCommandBuilder.BuildBuildArguments(spec), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, $"build image '{spec.Tag}'");
    }

    /// <inheritdoc/>
    public async Task<string> RunContainerAsync(ContainerRunSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var result = await TryRunAsync(CliCommandBuilder.BuildRunArguments(spec), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, $"start container '{spec.Name}'");
        return LastNonEmptyLine(result!.StandardOutput);
    }

    /// <inheritdoc/>
    public async Task EnsureNetworkAsync(NetworkCreateSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        // Idempotent: only create when the network is not already present.
        var inspect = await TryRunAsync(CliCommandBuilder.BuildNetworkInspectArguments(spec.Name), cancellationToken)
            .ConfigureAwait(false);
        if (inspect is { Success: true })
            return;

        var create = await TryRunAsync(CliCommandBuilder.BuildNetworkCreateArguments(spec), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(create, $"create network '{spec.Name}'");
    }

    /// <inheritdoc/>
    public async Task EnsureVolumeAsync(VolumeCreateSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var inspect = await TryRunAsync(CliCommandBuilder.BuildVolumeInspectArguments(spec.Name), cancellationToken)
            .ConfigureAwait(false);
        if (inspect is { Success: true })
            return;

        var create = await TryRunAsync(CliCommandBuilder.BuildVolumeCreateArguments(spec), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(create, $"create volume '{spec.Name}'");
    }

    /// <inheritdoc/>
    public async Task ConnectNetworkAsync(
        string network,
        string container,
        IReadOnlyList<string>? aliases,
        string? ipv4Address,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(network);
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        var result = await TryRunAsync(
                CliCommandBuilder.BuildNetworkConnectArguments(network, container, aliases, ipv4Address),
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result, $"connect container '{container}' to network '{network}'");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListNetworkNamesAsync(string projectName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        var result = await TryRunAsync(CliCommandBuilder.BuildNetworkListNamesArguments(projectName), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result, "list networks");
        return ParseNames(result!.StandardOutput);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListVolumeNamesAsync(string projectName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        var result = await TryRunAsync(CliCommandBuilder.BuildVolumeListNamesArguments(projectName), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result, "list volumes");
        return ParseNames(result!.StandardOutput);
    }

    /// <inheritdoc/>
    public async Task RemoveNetworkAsync(string network, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(network);

        var result = await TryRunAsync(CliCommandBuilder.BuildNetworkRemoveArguments(network), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result, $"remove network '{network}'");
    }

    /// <inheritdoc/>
    public async Task RemoveVolumeAsync(string volume, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volume);

        var result = await TryRunAsync(CliCommandBuilder.BuildVolumeRemoveArguments(volume), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result, $"remove volume '{volume}'");
    }

    /// <inheritdoc/>
    public async Task StopContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        var result = await TryRunAsync(CliCommandBuilder.BuildStopArguments(container), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, $"stop container '{container}'");
    }

    /// <inheritdoc/>
    public async Task StartContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        var result = await TryRunAsync(CliCommandBuilder.BuildStartArguments(container), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, $"start container '{container}'");
    }

    /// <inheritdoc/>
    public async Task RestartContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        var result = await TryRunAsync(CliCommandBuilder.BuildRestartArguments(container), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, $"restart container '{container}'");
    }

    /// <inheritdoc/>
    public async Task RemoveContainerAsync(string container, bool force, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        var result = await TryRunAsync(CliCommandBuilder.BuildRemoveArguments(container, force), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result, $"remove container '{container}'");
    }

    /// <inheritdoc/>
    public async Task<ContainerRuntimeState?> GetContainerStateAsync(string container, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        var result = await TryRunAsync(CliCommandBuilder.BuildInspectStateArguments(container), cancellationToken)
            .ConfigureAwait(false);

        // A missing container (inspect fails) is reported as "no state" rather than an error, so callers
        // can distinguish "not created yet" from a genuine failure.
        return result is { Success: true } ? CliStateParser.Parse(result.StandardOutput) : null;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(
        string? projectName,
        bool all,
        CancellationToken cancellationToken = default)
    {
        var result = await TryRunAsync(CliCommandBuilder.BuildPsArguments(projectName, all), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result, "list containers");
        return ParsePs(result!.StandardOutput);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><paramref name="container"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tail"/> is set and negative.</exception>
    public IAsyncEnumerable<ContainerLogLine> GetLogsAsync(
        string container,
        bool follow,
        int? tail,
        bool timestamps,
        string? since,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        if (tail is { } tailLines)
            ArgumentOutOfRangeException.ThrowIfNegative(tailLines, nameof(tail));

        return GetLogsCoreAsync(container, follow, tail, timestamps, since, cancellationToken);
    }

    private async IAsyncEnumerable<ContainerLogLine> GetLogsCoreAsync(
        string container,
        bool follow,
        int? tail,
        bool timestamps,
        string? since,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var arguments = CliCommandBuilder.BuildLogsArguments(container, follow, tail, timestamps, since);
        await foreach (var raw in ProcessRunner.StreamLinesAsync(Executable, arguments, cancellationToken).ConfigureAwait(false))
            yield return timestamps ? CliLogParser.ParseTimestamped(raw) : new ContainerLogLine(null, raw);
    }

    private Task<ProcessResult?> TryRunAsync(string arguments, CancellationToken cancellationToken)
        => ProcessRunner.TryRunAsync(Executable, arguments, cancellationToken);

    private void EnsureSuccess(ProcessResult? result, string action)
    {
        if (result is null)
            throw new ProviderException($"The '{Executable}' executable was not found on PATH.");

        if (result.Success)
            return;

        var detail = result.StandardError.Trim();
        if (detail.Length == 0)
            detail = result.StandardOutput.Trim();

        throw new ProviderException($"Failed to {action} using '{Executable}': {detail}");
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

    internal static IReadOnlyList<ContainerInfo> ParsePs(string output)
    {
        var containers = new List<ContainerInfo>();

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Trim().Length == 0)
                continue;

            var fields = line.Split(CliCommandBuilder.FieldSeparator);
            string Field(int index) => index < fields.Length ? fields[index].Trim() : string.Empty;

            var labels = Field(6);
            containers.Add(new ContainerInfo(
                Id: Field(0),
                Name: Field(1),
                Image: Field(2),
                State: Field(3),
                Status: Field(4),
                Service: ExtractLabel(labels, WslccLabels.Service),
                Ports: Field(5),
                Project: ExtractLabel(labels, WslccLabels.Project),
                ConfigHash: ExtractLabel(labels, WslccLabels.ConfigHash)));
        }

        return containers;
    }

    /// <summary>Parses a newline-separated list of resource names (from a <c>--format {{.Name}}</c> listing).</summary>
    internal static IReadOnlyList<string> ParseNames(string output)
    {
        var names = new List<string>();
        foreach (var rawLine in output.Split('\n'))
        {
            var name = rawLine.Trim();
            if (name.Length > 0)
                names.Add(name);
        }

        return names;
    }

    private static string? ExtractLabel(string labels, string key)
    {
        // Labels come as a comma-separated "k=v" list.
        foreach (var pair in labels.Split(','))
        {
            var trimmed = pair.Trim();
            if (trimmed.StartsWith(key + "=", StringComparison.Ordinal))
                return trimmed.Substring(key.Length + 1);
        }

        return null;
    }
}

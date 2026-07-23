using Wslcc.Abstractions;

namespace Wslcc.Providers.Common;

/// <summary>
/// Base class for providers that drive a standard container CLI (docker, wslc). Container operations
/// are identical across those tools, so subclasses only supply the executable name, the provider name,
/// and how to report version/availability.
/// </summary>
public abstract class CliContainerProviderBase : IContainerProvider
{
    /// <summary>The executable to invoke (e.g. "docker", "wslc").</summary>
    protected abstract string Executable { get; }

    public abstract string Name { get; }

    public abstract Task<ProviderInfo> GetProviderInfoAsync(CancellationToken cancellationToken = default);

    public async Task EnsureImageAsync(string image, bool alwaysPull, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            throw new ProviderException("No image specified.");
        }

        if (!alwaysPull)
        {
            var inspect = await TryRunAsync(CliCommandBuilder.BuildImageInspectArguments(image), cancellationToken)
                .ConfigureAwait(false);
            if (inspect is { Success: true })
            {
                return;
            }
        }

        var pull = await TryRunAsync(CliCommandBuilder.BuildPullArguments(image), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(pull, $"pull image '{image}'");
    }

    public async Task BuildImageAsync(ImageBuildSpec spec, CancellationToken cancellationToken = default)
    {
        var result = await TryRunAsync(CliCommandBuilder.BuildBuildArguments(spec), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, $"build image '{spec.Tag}'");
    }

    public async Task<string> RunContainerAsync(ContainerRunSpec spec, CancellationToken cancellationToken = default)
    {
        var result = await TryRunAsync(CliCommandBuilder.BuildRunArguments(spec), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, $"start container '{spec.Name}'");
        return LastNonEmptyLine(result!.StandardOutput);
    }

    public async Task StopContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        var result = await TryRunAsync(CliCommandBuilder.BuildStopArguments(container), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, $"stop container '{container}'");
    }

    public async Task StartContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        var result = await TryRunAsync(CliCommandBuilder.BuildStartArguments(container), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, $"start container '{container}'");
    }

    public async Task RestartContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        var result = await TryRunAsync(CliCommandBuilder.BuildRestartArguments(container), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, $"restart container '{container}'");
    }

    public async Task RemoveContainerAsync(string container, bool force, CancellationToken cancellationToken = default)
    {
        var result = await TryRunAsync(CliCommandBuilder.BuildRemoveArguments(container, force), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result, $"remove container '{container}'");
    }

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

    public IAsyncEnumerable<string> GetLogsAsync(
        string container,
        bool follow,
        int? tail,
        CancellationToken cancellationToken = default)
        => ProcessRunner.StreamLinesAsync(Executable, CliCommandBuilder.BuildLogsArguments(container, follow, tail), cancellationToken);

    private Task<ProcessResult?> TryRunAsync(string arguments, CancellationToken cancellationToken)
        => ProcessRunner.TryRunAsync(Executable, arguments, cancellationToken);

    private void EnsureSuccess(ProcessResult? result, string action)
    {
        if (result is null)
        {
            throw new ProviderException($"The '{Executable}' executable was not found on PATH.");
        }

        if (!result.Success)
        {
            var detail = result.StandardError.Trim();
            if (detail.Length == 0)
            {
                detail = result.StandardOutput.Trim();
            }

            throw new ProviderException($"Failed to {action} using '{Executable}': {detail}");
        }
    }

    private static string LastNonEmptyLine(string output)
    {
        var lines = output.Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length > 0)
            {
                return line;
            }
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
            {
                continue;
            }

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
                Project: ExtractLabel(labels, WslccLabels.Project)));
        }

        return containers;
    }

    private static string? ExtractLabel(string labels, string key)
    {
        // Labels come as a comma-separated "k=v" list.
        foreach (var pair in labels.Split(','))
        {
            var trimmed = pair.Trim();
            if (trimmed.StartsWith(key + "=", StringComparison.Ordinal))
            {
                return trimmed.Substring(key.Length + 1);
            }
        }

        return null;
    }
}

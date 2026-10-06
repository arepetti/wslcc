using System.Text;
using Wslcc.Abstractions;
using Wslcc.Providers.Common;

namespace Wslcc.Providers.Wslc;

/// <summary>
/// Builds commands for the GA WSL containers CLI. WSLc deliberately has its own dialect because its
/// JSON formatting, network aliases, and supported run options differ from Docker.
/// </summary>
public static class WslcCommandBuilder
{
    public static string BuildRunArguments(ContainerRunSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.Annotations.Count > 0)
            throw Unsupported("OCI annotations");

        if (spec.ReadOnly)
            throw Unsupported("a read-only root filesystem");

        if (!string.IsNullOrWhiteSpace(spec.Restart))
            throw Unsupported("restart policies");

        if (string.Equals(spec.Network, "host", StringComparison.OrdinalIgnoreCase))
            throw Unsupported("host networking");

        return WithSession(CliCommandBuilder.BuildRunArguments(spec));
    }

    public static string BuildBuildArguments(ImageBuildSpec spec)
        => WithSession(CliCommandBuilder.BuildBuildArguments(spec));

    public static string BuildPsArguments(string? projectName, bool all)
    {
        var args = new List<string> { "ps" };
        if (all)
            args.Add("--all");

        args.Add("--filter");
        args.Add(projectName is null
            ? $"label={WslccLabels.Project}"
            : $"label={WslccLabels.Project}={projectName}");
        args.Add("--format");
        args.Add("json");
        return WithSession(args);
    }

    public static string BuildNetworkInspectArguments(string network)
        => WithSession(CliCommandBuilder.BuildNetworkInspectArguments(network));

    public static string BuildNetworkCreateArguments(NetworkCreateSpec spec)
        => WithSession(CliCommandBuilder.BuildNetworkCreateArguments(spec));

    public static string BuildNetworkConnectArguments(
        string network,
        string container,
        IReadOnlyList<string>? aliases,
        string? ipv4Address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(network);
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        var args = new List<string> { "network", "connect" };
        foreach (var alias in aliases ?? Array.Empty<string>())
        {
            args.Add("--network-alias");
            args.Add(alias);
        }

        if (!string.IsNullOrWhiteSpace(ipv4Address))
        {
            args.Add("--ip");
            args.Add(ipv4Address!);
        }

        args.Add(network);
        args.Add(container);
        return WithSession(args);
    }

    public static string BuildNetworkListArguments(string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        return WithSession(new[]
        {
            "network", "ls", "--filter", $"label={WslccLabels.Project}={projectName}", "--format", "json",
        });
    }

    public static string BuildNetworkRemoveArguments(string network)
        => WithSession(CliCommandBuilder.BuildNetworkRemoveArguments(network));

    public static string BuildVolumeInspectArguments(string volume)
        => WithSession(CliCommandBuilder.BuildVolumeInspectArguments(volume));

    public static string BuildVolumeCreateArguments(VolumeCreateSpec spec)
        => WithSession(CliCommandBuilder.BuildVolumeCreateArguments(spec));

    public static string BuildVolumeListArguments(string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        return WithSession(new[]
        {
            "volume", "ls", "--filter", $"label={WslccLabels.Project}={projectName}", "--format", "json",
        });
    }

    public static string BuildVolumeRemoveArguments(string volume)
        => WithSession(CliCommandBuilder.BuildVolumeRemoveArguments(volume));

    public static string BuildLogsArguments(
        string container,
        bool follow,
        int? tail,
        bool timestamps,
        string? since)
        => WithSession(CliCommandBuilder.BuildLogsArguments(container, follow, tail, timestamps, since));

    private static ProviderException Unsupported(string feature)
        => new($"The WSLc 3.0.1 provider does not support {feature}.");

    private static string WithSession(string arguments)
        => $"--session {Quote(WslcSdkClient.SessionName)} {arguments}";

    private static string WithSession(IEnumerable<string> arguments)
        => $"--session {Quote(WslcSdkClient.SessionName)} {Join(arguments)}";

    private static string Join(IEnumerable<string> arguments)
        => string.Join(" ", arguments.Select(Quote));

    private static string Quote(string value)
    {
        if (value.Length == 0)
            return "\"\"";

        if (value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            return value;

        var result = new StringBuilder(value.Length + 2);
        result.Append('"');
        foreach (var character in value)
        {
            if (character == '"')
                result.Append('\\');

            result.Append(character);
        }

        result.Append('"');
        return result.ToString();
    }
}

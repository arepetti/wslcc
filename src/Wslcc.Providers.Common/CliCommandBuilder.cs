using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Wslcc.Abstractions;
using Wslcc.Abstractions.Compose;

namespace Wslcc.Providers.Common;

/// <summary>
/// Builds Docker-compatible argument strings. WSLc reuses individual compatible builders but owns a
/// separate dialect for JSON listing, session scoping, aliases, and capability validation.
/// </summary>
/// <remarks>
/// Every method returns a single argument string with each token quoted as needed, ready to hand to
/// <see cref="ProcessRunner"/>. Listing commands filter on the <see cref="WslccLabels"/> keys, so they
/// only ever see wslcc-managed resources.
/// </remarks>
/// <example>
/// Running a container and then reading its logs:
/// <code>
/// var runArgs = CliCommandBuilder.BuildRunArguments(spec);
/// // e.g. run -d --name myproject-web --label wslcc.project=myproject -p 8080:80 nginx:alpine
/// var result = await ProcessRunner.TryRunAsync("docker", runArgs, cancellationToken);
///
/// var logArgs = CliCommandBuilder.BuildLogsArguments(
///     "myproject-web", follow: true, tail: 100, timestamps: true, since: "10m");
/// await foreach (var line in ProcessRunner.StreamLinesAsync("docker", logArgs, cancellationToken))
/// {
///     Console.WriteLine(CliLogParser.ParseTimestamped(line).Message);
/// }
/// </code>
/// </example>
public static class CliCommandBuilder
{
    /// <summary>Field separator used in the <c>ps --format</c> template (ASCII unit separator).</summary>
    public const char FieldSeparator = '\u001f';

    /// <summary>Go-template used for <c>ps</c> output. Fields: id, names, image, state, status, ports, labels.</summary>
    public static readonly string PsFormat = string.Join(
        FieldSeparator,
        "{{.ID}}", "{{.Names}}", "{{.Image}}", "{{.State}}", "{{.Status}}", "{{.Ports}}", "{{.Labels}}");

    /// <summary>
    /// Builds the <c>run</c> arguments for a container. Options are emitted in the order the CLI expects
    /// them, followed by the image and the argv it should run.
    /// </summary>
    /// <param name="spec">The container to create.</param>
    /// <returns>The quoted argument string, starting with <c>run</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <c>null</c>.</exception>
    /// <exception cref="ProviderException">The spec has no image to run.</exception>
    public static string BuildRunArguments(ContainerRunSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (string.IsNullOrWhiteSpace(spec.Image))
            throw new ProviderException($"Service '{spec.Name}' has no image.");

        var args = new List<string> { "run" };

        AppendIdentity(args, spec);
        AppendNetwork(args, spec);
        AppendEnvironment(args, spec);
        AppendPortsAndVolumes(args, spec);
        AppendProcessOptions(args, spec);
        AppendHealthCheck(args, spec.HealthCheck);

        args.Add(spec.Image);
        AppendArgv(args, spec);

        return Join(args);
    }

    /// <summary>Compose file — container_name / labels / annotations: name, labels, and OCI annotations.</summary>
    private static void AppendIdentity(List<string> args, ContainerRunSpec spec)
    {
        if (spec.Detach)
            args.Add("-d");

        if (!string.IsNullOrEmpty(spec.Name))
        {
            args.Add("--name");
            args.Add(spec.Name);
        }

        foreach (var label in spec.Labels)
        {
            args.Add("--label");
            args.Add($"{label.Key}={label.Value}");
        }

        foreach (var annotation in spec.Annotations)
        {
            args.Add("--annotation");
            args.Add($"{annotation.Key}={annotation.Value}");
        }
    }

    /// <summary>
    /// Compose file — networks: the container is created on a single network; any others are connected
    /// afterwards. The alias is only meaningful alongside a network, so it is emitted with it.
    /// </summary>
    private static void AppendNetwork(List<string> args, ContainerRunSpec spec)
    {
        if (string.IsNullOrEmpty(spec.Network))
            return;

        args.Add("--network");
        args.Add(spec.Network!);

        if (!string.IsNullOrEmpty(spec.NetworkAlias))
        {
            args.Add("--network-alias");
            args.Add(spec.NetworkAlias!);
        }

        foreach (var alias in spec.NetworkAliases.Where(alias => !string.Equals(alias, spec.NetworkAlias, StringComparison.Ordinal)))
        {
            args.Add("--network-alias");
            args.Add(alias);
        }

        AddOption(args, "--ip", spec.NetworkIPv4Address);
    }

    /// <summary>
    /// Compose file — env_file / environment: env files come before <c>-e</c> so <c>environment:</c> keys
    /// override file contents (Compose / docker run order). A value-less key passes through so the
    /// runtime can inherit it from the host.
    /// </summary>
    private static void AppendEnvironment(List<string> args, ContainerRunSpec spec)
    {
        foreach (var envFile in spec.EnvFiles)
        {
            args.Add("--env-file");
            args.Add(envFile);
        }

        foreach (var env in spec.Environment)
        {
            args.Add("-e");
            args.Add(env.Value is null ? env.Key : $"{env.Key}={env.Value}");
        }
    }

    /// <summary>
    /// Compose file — ports / volumes / tmpfs: ports as <c>-p</c>; mounts as <c>-v</c>, <c>--mount</c>, or
    /// <c>--tmpfs</c> depending on type and option blocks.
    /// </summary>
    private static void AppendPortsAndVolumes(List<string> args, ContainerRunSpec spec)
    {
        foreach (var port in spec.Ports)
        {
            args.Add("-p");
            args.Add(port);
        }

        foreach (var mount in spec.Volumes)
            AppendMount(args, mount);
    }

    private static void AppendMount(List<string> args, ServiceMount mount)
    {
        if (mount.Type is MountType.Tmpfs)
        {
            args.Add("--tmpfs");
            args.Add(FormatTmpfs(mount));
            return;
        }

        if (mount.RequiresMountFlag)
        {
            args.Add("--mount");
            args.Add(FormatMountFlag(mount));
            return;
        }

        args.Add("-v");
        args.Add(FormatVolumeShort(mount));
    }

    /// <summary>Formats a plain volume/bind for <c>-v</c> (<c>source:target[:mode]</c> or anonymous target).</summary>
    private static string FormatVolumeShort(ServiceMount mount)
    {
        if (string.IsNullOrEmpty(mount.Source))
            return mount.Target;

        var modes = new List<string>();
        if (mount.ReadOnly)
            modes.Add("ro");

        // SELinux relabel flags are short-syntax only on docker run -v (not --mount).
        if (!string.IsNullOrEmpty(mount.BindSelinux))
            modes.Add(mount.BindSelinux);

        return modes.Count == 0
            ? $"{mount.Source}:{mount.Target}"
            : $"{mount.Source}:{mount.Target}:{string.Join(',', modes)}";
    }

    private static string FormatMountFlag(ServiceMount mount)
    {
        var parts = new List<string>
        {
            $"type={MountTypeName(mount.Type)}",
        };

        if (!string.IsNullOrEmpty(mount.Source))
            parts.Add($"source={mount.Source}");

        parts.Add($"target={mount.Target}");

        if (mount.ReadOnly)
            parts.Add("readonly");

        if (mount.VolumeNocopy == true)
            parts.Add("volume-nocopy");

        if (!string.IsNullOrEmpty(mount.VolumeSubpath))
            parts.Add($"volume-subpath={mount.VolumeSubpath}");

        if (!string.IsNullOrEmpty(mount.BindPropagation))
            parts.Add($"bind-propagation={mount.BindPropagation}");

        if (!string.IsNullOrEmpty(mount.BindRecursive))
            parts.Add($"bind-recursive={mount.BindRecursive}");

        return string.Join(',', parts);
    }

    private static string FormatTmpfs(ServiceMount mount)
    {
        var opts = new List<string>();
        if (!string.IsNullOrEmpty(mount.TmpfsSize))
            opts.Add($"size={mount.TmpfsSize}");

        if (!string.IsNullOrEmpty(mount.TmpfsMode))
            opts.Add($"mode={mount.TmpfsMode}");

        if (!string.IsNullOrEmpty(mount.TmpfsExtraOptions))
            opts.Add(mount.TmpfsExtraOptions);

        if (mount.ReadOnly)
            opts.Add("ro");

        return opts.Count == 0 ? mount.Target : $"{mount.Target}:{string.Join(',', opts)}";
    }

    private static string MountTypeName(MountType type) => type switch
    {
        MountType.Bind => "bind",
        MountType.Tmpfs => "tmpfs",
        _ => "volume",
    };

    /// <summary>
    /// Compose file — user / working_dir / entrypoint / restart. Only the first entrypoint token can be
    /// given as <c>--entrypoint</c>; the rest are emitted after the image by <see cref="AppendArgv"/>.
    /// </summary>
    private static void AppendProcessOptions(List<string> args, ContainerRunSpec spec)
    {
        AddOption(args, "-u", spec.User);
        AddOption(args, "-w", spec.WorkingDir);
        // Compose file — hostname: container UTS name (--hostname).
        AddOption(args, "--hostname", spec.Hostname);
        AddOption(args, "--domainname", spec.DomainName);
        AddOption(args, "--gpus", spec.Gpus);
        AddOption(args, "--cpus", spec.Cpus);
        AddOption(args, "--memory", spec.MemoryLimit);
        AddOption(args, "--shm-size", spec.ShmSize);
        AddOption(args, "--stop-signal", spec.StopSignal);
        AddOption(args, "--stop-timeout", DurationToSeconds(spec.StopGracePeriod));

        foreach (var dns in spec.Dns)
            AddOption(args, "--dns", dns);
        foreach (var option in spec.DnsOptions)
            AddOption(args, "--dns-option", option);
        foreach (var search in spec.DnsSearch)
            AddOption(args, "--dns-search", search);
        foreach (var limit in spec.Ulimits)
            AddOption(args, "--ulimit", $"{limit.Key}={limit.Value}");

        if (spec.StdinOpen)
            args.Add("-i");
        if (spec.Tty)
            args.Add("-t");

        if (spec.ReadOnly)
            args.Add("--read-only");

        if (spec.Entrypoint.Count > 0)
        {
            args.Add("--entrypoint");
            args.Add(spec.Entrypoint[0]);
        }

        AddOption(args, "--restart", spec.Restart);
    }

    /// <summary>
    /// Compose file — entrypoint / command: the entrypoint tokens after the first become CMD ahead of
    /// <c>command:</c>, producing the same final argv as a multi-argument ENTRYPOINT.
    /// </summary>
    private static void AppendArgv(List<string> args, ContainerRunSpec spec)
    {
        for (var i = 1; i < spec.Entrypoint.Count; i++)
            args.Add(spec.Entrypoint[i]);

        foreach (var token in spec.Command)
            args.Add(token);
    }

    /// <summary>
    /// Builds the <c>ps</c> arguments, filtered to wslcc-managed containers and formatted with
    /// <see cref="PsFormat"/>.
    /// </summary>
    /// <param name="projectName">Project to scope to, or <c>null</c> for every wslcc-managed container.</param>
    /// <param name="all">Include stopped containers, not just running ones.</param>
    /// <returns>The quoted argument string, starting with <c>ps</c>.</returns>
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
        args.Add(PsFormat);

        return Join(args);
    }

    /// <summary>Builds the <c>pull</c> arguments for an image.</summary>
    /// <param name="image">Image reference to pull.</param>
    /// <returns>The quoted argument string, starting with <c>pull</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="image"/> is null, empty or whitespace.</exception>
    public static string BuildPullArguments(string image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);

        return Join(new[] { "pull", image });
    }

    /// <summary>Builds the <c>build</c> arguments for an image, with the context as the final argument.</summary>
    /// <param name="spec">The build context, Dockerfile, target stage, build args and tag.</param>
    /// <returns>The quoted argument string, starting with <c>build</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <c>null</c>.</exception>
    /// <exception cref="ProviderException">The spec has no build context.</exception>
    public static string BuildBuildArguments(ImageBuildSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (string.IsNullOrWhiteSpace(spec.Context))
            throw new ProviderException("No build context specified.");

        var args = new List<string> { "build" };

        if (!string.IsNullOrWhiteSpace(spec.Tag))
        {
            args.Add("-t");
            args.Add(spec.Tag);
        }

        if (!string.IsNullOrWhiteSpace(spec.Dockerfile))
        {
            args.Add("-f");
            args.Add(spec.Dockerfile!);
        }

        if (!string.IsNullOrWhiteSpace(spec.Target))
        {
            args.Add("--target");
            args.Add(spec.Target!);
        }

        foreach (var arg in spec.Args)
        {
            args.Add("--build-arg");
            args.Add(arg.Value is null ? arg.Key : $"{arg.Key}={arg.Value}");
        }

        foreach (var label in spec.Labels)
        {
            args.Add("--label");
            args.Add($"{label.Key}={label.Value}");
        }

        if (spec.NoCache)
            args.Add("--no-cache");

        if (spec.Pull)
            args.Add("--pull");

        foreach (var secret in spec.Secrets)
        {
            args.Add("--secret");
            args.Add(secret);
        }

        args.Add(spec.Context);

        return Join(args);
    }

    /// <summary>
    /// Compose file — healthcheck: <c>disable</c> (or <c>test: ["NONE"]</c>) becomes
    /// <c>--no-healthcheck</c>; otherwise each field that was specified becomes its <c>--health-*</c>
    /// flag and the rest are left to the image's own healthcheck.
    /// </summary>
    private static void AppendHealthCheck(List<string> args, ContainerHealthCheck? health)
    {
        if (health is null)
            return;

        if (health.Disabled)
        {
            args.Add("--no-healthcheck");
            return;
        }

        AddOption(args, "--health-cmd", health.Command);
        AddOption(args, "--health-interval", health.Interval);
        AddOption(args, "--health-timeout", health.Timeout);
        AddOption(args, "--health-retries", health.Retries?.ToString(CultureInfo.InvariantCulture));
        AddOption(args, "--health-start-period", health.StartPeriod);
    }

    /// <summary>Appends <paramref name="option"/> and its value; a null/blank value means "not specified".</summary>
    private static void AddOption(List<string> args, string option, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        args.Add(option);
        args.Add(value!);
    }

    /// <summary>Builds the <c>image inspect</c> arguments; a non-zero exit means the image is not present.</summary>
    /// <param name="image">Image reference to inspect.</param>
    /// <returns>The quoted argument string, starting with <c>image inspect</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="image"/> is null, empty or whitespace.</exception>
    public static string BuildImageInspectArguments(string image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);

        return Join(new[] { "image", "inspect", image });
    }

    /// <summary>Go-template used by <c>container inspect</c> for runtime state. Fields: status, health, exit code.</summary>
    public static readonly string InspectStateFormat = string.Join(
        FieldSeparator,
        "{{.State.Status}}", "{{if .State.Health}}{{.State.Health.Status}}{{end}}", "{{.State.ExitCode}}");

    /// <summary>
    /// Builds the <c>container inspect</c> arguments that produce the runtime state
    /// <see cref="CliStateParser"/> reads.
    /// </summary>
    /// <param name="container">Container id or name.</param>
    /// <returns>The quoted argument string, starting with <c>container inspect</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="container"/> is null, empty or whitespace.</exception>
    public static string BuildInspectStateArguments(string container)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        return Join(new[] { "container", "inspect", "--format", InspectStateFormat, container });
    }

    /// <summary>Builds the <c>network create</c> arguments, with the network name as the final argument.</summary>
    /// <param name="spec">The network to create, with its driver and project labels.</param>
    /// <returns>The quoted argument string, starting with <c>network create</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The spec has no name.</exception>
    public static string BuildNetworkCreateArguments(NetworkCreateSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Name);

        var args = new List<string> { "network", "create" };
        if (!string.IsNullOrWhiteSpace(spec.Driver))
        {
            args.Add("--driver");
            args.Add(spec.Driver!);
        }

        foreach (var label in spec.Labels)
        {
            args.Add("--label");
            args.Add($"{label.Key}={label.Value}");
        }

        foreach (var option in spec.DriverOptions)
        {
            args.Add("--opt");
            args.Add($"{option.Key}={option.Value}");
        }

        if (spec.Internal)
            args.Add("--internal");

        AddOption(args, "--subnet", spec.Subnet);
        AddOption(args, "--gateway", spec.Gateway);
        AddOption(args, "--ip-range", spec.IpRange);

        args.Add(spec.Name);
        return Join(args);
    }

    /// <summary>Builds the <c>network inspect</c> arguments; a non-zero exit means the network does not exist.</summary>
    /// <param name="network">Network name.</param>
    /// <returns>The quoted argument string, starting with <c>network inspect</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="network"/> is null, empty or whitespace.</exception>
    public static string BuildNetworkInspectArguments(string network)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(network);

        return Join(new[] { "network", "inspect", network });
    }

    /// <summary>Builds the <c>network rm</c> arguments.</summary>
    /// <param name="network">Network name.</param>
    /// <returns>The quoted argument string, starting with <c>network rm</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="network"/> is null, empty or whitespace.</exception>
    public static string BuildNetworkRemoveArguments(string network)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(network);

        return Join(new[] { "network", "rm", network });
    }

    /// <summary>Builds the <c>network connect</c> arguments for attaching a container to a further network.</summary>
    /// <param name="network">Network to connect to.</param>
    /// <param name="container">Container to connect.</param>
    /// <param name="alias">Alias to publish on the network; <c>null</c>/blank emits no <c>--alias</c>.</param>
    /// <returns>The quoted argument string, starting with <c>network connect</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="network"/> or <paramref name="container"/> is null, empty or whitespace.</exception>
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
            args.Add("--alias");
            args.Add(alias);
        }

        AddOption(args, "--ip", ipv4Address);

        args.Add(network);
        args.Add(container);
        return Join(args);
    }

    /// <summary>Builds the <c>network ls</c> arguments listing just the names of a project's networks.</summary>
    /// <param name="projectName">Project to filter on.</param>
    /// <returns>The quoted argument string, starting with <c>network ls</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="projectName"/> is null, empty or whitespace.</exception>
    public static string BuildNetworkListNamesArguments(string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        return Join(new[]
        {
            "network", "ls", "--filter", $"label={WslccLabels.Project}={projectName}", "--format", "{{.Name}}",
        });
    }

    /// <summary>Builds the <c>volume create</c> arguments, with the volume name as the final argument.</summary>
    /// <param name="spec">The volume to create, with its driver and project labels.</param>
    /// <returns>The quoted argument string, starting with <c>volume create</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The spec has no name.</exception>
    public static string BuildVolumeCreateArguments(VolumeCreateSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Name);

        var args = new List<string> { "volume", "create" };
        if (!string.IsNullOrWhiteSpace(spec.Driver))
        {
            args.Add("--driver");
            args.Add(spec.Driver!);
        }

        foreach (var label in spec.Labels)
        {
            args.Add("--label");
            args.Add($"{label.Key}={label.Value}");
        }

        foreach (var option in spec.DriverOptions)
        {
            args.Add("--opt");
            args.Add($"{option.Key}={option.Value}");
        }

        args.Add(spec.Name);
        return Join(args);
    }

    /// <summary>Builds the <c>volume inspect</c> arguments; a non-zero exit means the volume does not exist.</summary>
    /// <param name="volume">Volume name.</param>
    /// <returns>The quoted argument string, starting with <c>volume inspect</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="volume"/> is null, empty or whitespace.</exception>
    public static string BuildVolumeInspectArguments(string volume)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volume);

        return Join(new[] { "volume", "inspect", volume });
    }

    /// <summary>Builds the <c>volume rm</c> arguments.</summary>
    /// <param name="volume">Volume name.</param>
    /// <returns>The quoted argument string, starting with <c>volume rm</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="volume"/> is null, empty or whitespace.</exception>
    public static string BuildVolumeRemoveArguments(string volume)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volume);

        return Join(new[] { "volume", "rm", volume });
    }

    /// <summary>Builds the <c>volume ls</c> arguments listing just the names of a project's volumes.</summary>
    /// <param name="projectName">Project to filter on.</param>
    /// <returns>The quoted argument string, starting with <c>volume ls</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="projectName"/> is null, empty or whitespace.</exception>
    public static string BuildVolumeListNamesArguments(string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        return Join(new[]
        {
            "volume", "ls", "--filter", $"label={WslccLabels.Project}={projectName}", "--format", "{{.Name}}",
        });
    }

    /// <summary>Builds the <c>stop</c> arguments for a container.</summary>
    /// <param name="container">Container id or name.</param>
    /// <returns>The quoted argument string, starting with <c>stop</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="container"/> is null, empty or whitespace.</exception>
    public static string BuildStopArguments(string container)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        return Join(new[] { "stop", container });
    }

    /// <summary>Builds the <c>start</c> arguments for an existing container.</summary>
    /// <param name="container">Container id or name.</param>
    /// <returns>The quoted argument string, starting with <c>start</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="container"/> is null, empty or whitespace.</exception>
    public static string BuildStartArguments(string container)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        return Join(new[] { "start", container });
    }

    /// <summary>Builds the <c>restart</c> arguments for a container.</summary>
    /// <param name="container">Container id or name.</param>
    /// <returns>The quoted argument string, starting with <c>restart</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="container"/> is null, empty or whitespace.</exception>
    public static string BuildRestartArguments(string container)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        return Join(new[] { "restart", container });
    }

    /// <summary>Builds the <c>rm</c> arguments for a container.</summary>
    /// <param name="container">Container id or name.</param>
    /// <param name="force">Emit <c>-f</c> so a still-running container is removed too.</param>
    /// <returns>The quoted argument string, starting with <c>rm</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="container"/> is null, empty or whitespace.</exception>
    public static string BuildRemoveArguments(string container, bool force)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        return force ? Join(new[] { "rm", "-f", container }) : Join(new[] { "rm", container });
    }

    /// <summary>Builds the <c>logs</c> arguments for a container.</summary>
    /// <param name="container">Container id or name.</param>
    /// <param name="follow">Emit <c>--follow</c> so the CLI keeps streaming new lines.</param>
    /// <param name="tail">Emit <c>--tail</c> to limit the initial output; <c>null</c> means all of it.</param>
    /// <param name="timestamps">Emit <c>--timestamps</c>, which <see cref="CliLogParser"/> then strips off.</param>
    /// <param name="since">Emit <c>--since</c> with a duration or RFC3339 timestamp; <c>null</c>/blank omits it.</param>
    /// <returns>The quoted argument string, starting with <c>logs</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="container"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tail"/> is set and negative.</exception>
    public static string BuildLogsArguments(string container, bool follow, int? tail, bool timestamps = false, string? since = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        if (tail is { } tailLines)
            ArgumentOutOfRangeException.ThrowIfNegative(tailLines, nameof(tail));

        var args = new List<string> { "logs" };

        if (follow)
            args.Add("--follow");

        if (timestamps)
            args.Add("--timestamps");

        if (!string.IsNullOrWhiteSpace(since))
        {
            args.Add("--since");
            args.Add(since!);
        }

        if (tail is { } n)
        {
            args.Add("--tail");
            args.Add(n.ToString(CultureInfo.InvariantCulture));
        }

        args.Add(container);

        return Join(args);
    }

    internal static string Join(IEnumerable<string> args)
        => string.Join(" ", args.Select(Quote));

    internal static string Quote(string value)
    {
        if (value.Length == 0)
            return "\"\"";

        var needsQuotes = value.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0;
        if (!needsQuotes)
            return value;

        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            if (c == '"')
                sb.Append('\\');

            sb.Append(c);
        }

        sb.Append('"');
        return sb.ToString();
    }

    private static string? DurationToSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return seconds.ToString(CultureInfo.InvariantCulture);

        var matches = Regex.Matches(value, @"(?<value>\d+(?:\.\d+)?)(?<unit>ms|us|ns|h|m|s)");
        if (matches.Count == 0 || string.Concat(matches.Select(match => match.Value)) != value)
            return value;

        var totalSeconds = 0d;
        foreach (Match match in matches)
        {
            var amount = double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
            totalSeconds += match.Groups["unit"].Value switch
            {
                "h" => amount * 3600,
                "m" => amount * 60,
                "s" => amount,
                "ms" => amount / 1000,
                "us" => amount / 1_000_000,
                "ns" => amount / 1_000_000_000,
                _ => 0,
            };
        }

        return Math.Ceiling(totalSeconds).ToString(CultureInfo.InvariantCulture);
    }
}

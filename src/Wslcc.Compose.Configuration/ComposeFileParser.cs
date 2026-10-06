using Wslcc.Abstractions.Compose;
using YamlDotNet.Serialization;

namespace Wslcc.Compose.Configuration;

/// <summary>
/// Parses Compose YAML into <see cref="ComposeFile"/>. This is a tolerant parser that understands
/// the common short/long forms of the most-used keys. Full Compose specification fidelity is tracked
/// in docs/todo.md.
/// </summary>
/// <remarks>
/// The parser expects an already-resolved document: merging, <c>${VAR}</c> interpolation,
/// <c>extends</c> and profile filtering are <see cref="ComposeLoader"/>'s job. An instance is cheap and
/// holds only its YAML deserializer, so it can be reused across documents.
/// </remarks>
/// <example>
/// Parsing a document and inspecting a service:
/// <code>
/// var parser = new ComposeFileParser();
/// ComposeFile file = parser.Parse("""
///     services:
///       web:
///         image: nginx:alpine
///         command: echo hello
///     """);
///
/// // file.Services["web"].Image   == "nginx:alpine"
/// // file.Services["web"].Command == ["/bin/sh", "-c", "echo hello"]  (string form is shell form)
/// </code>
/// </example>
public sealed class ComposeFileParser
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder().Build();

    /// <summary>Reads and parses a Compose file from disk.</summary>
    /// <param name="path">Path to the Compose document.</param>
    /// <returns>The parsed file; an empty document yields an empty <see cref="ComposeFile"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty or whitespace.</exception>
    /// <exception cref="ComposeLoadException">The document is invalid YAML or uses an unsupported form.</exception>
    public ComposeFile ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Parse(File.ReadAllText(path), path);
    }

    /// <summary>Parses an already-resolved Compose document (merged, interpolated, profile-filtered).</summary>
    /// <param name="yaml">The Compose YAML.</param>
    /// <param name="source">Optional origin of the document, for diagnostics.</param>
    /// <returns>The parsed file; an empty or non-map document yields an empty <see cref="ComposeFile"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/> is <c>null</c>.</exception>
    /// <exception cref="ComposeLoadException">
    /// The document is invalid YAML, or a service uses a form the parser rejects (e.g. an unsupported
    /// volume <c>type</c>, malformed long-form port, or malformed <c>env_file</c> entry).
    /// </exception>
    public ComposeFile Parse(string yaml, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var map = AsMap(_deserializer.Deserialize<object?>(yaml));
        var file = new ComposeFile();

        if (map is null)
            return file;

        // Compose file — name: the project name declared in the document (a CLI -p flag still wins).
        file.Name = GetString(map, "name");

        ParseSection(GetValue(map, "services"), file.Services, ParseService);
        ParseSection(GetValue(map, "networks"), file.Networks, ParseNetwork);
        ParseSection(GetValue(map, "volumes"), file.Volumes, ParseVolume);
        ParseSection(GetValue(map, "secrets"), file.Secrets, ParseSecret);
        ValidateSecretAttachments(file);

        return file;
    }

    /// <summary>
    /// Parses a top-level map section (<c>services</c> / <c>networks</c> / <c>volumes</c> / <c>secrets</c>) into
    /// <paramref name="target"/>, keyed by the entry's name. A missing section — or one that is not a
    /// map — leaves the target empty rather than failing the parse.
    /// </summary>
    private static void ParseSection<T>(object? value, IDictionary<string, T> target, Func<string, object?, T> parse)
    {
        if (AsMap(value) is not { } section)
            return;

        foreach (var kvp in section)
            target[kvp.Key] = parse(kvp.Key, kvp.Value);
    }

    private static ServiceSpec ParseService(string name, object? value)
    {
        var service = new ServiceSpec { Name = name };
        var map = AsMap(value);
        if (map is null)
            return service;

        service.Image = GetString(map, "image");
        service.ContainerName = GetString(map, "container_name");
        service.Restart = GetString(map, "restart");
        service.WorkingDir = GetString(map, "working_dir");
        service.User = GetString(map, "user");
        service.Hostname = GetString(map, "hostname");
        service.DomainName = GetString(map, "domainname");
        service.Gpus = ScalarOrJoined(GetValue(map, "gpus"));
        service.Cpus = GetString(map, "cpus");
        service.MemoryLimit = GetString(map, "mem_limit");
        service.Dns = ToStringList(GetValue(map, "dns"));
        service.DnsOptions = ToStringList(GetValue(map, "dns_opt"));
        service.DnsSearch = ToStringList(GetValue(map, "dns_search"));
        service.ShmSize = GetString(map, "shm_size");
        service.StdinOpen = GetBool(map, "stdin_open");
        service.Tty = GetBool(map, "tty");
        service.Ulimits = ParseUlimits(GetValue(map, "ulimits"));
        service.StopSignal = GetString(map, "stop_signal");
        service.StopGracePeriod = GetString(map, "stop_grace_period");
        service.NetworkMode = GetString(map, "network_mode");
        service.ReadOnly = GetBool(map, "read_only");

        service.Build = ParseBuild(GetValue(map, "build"));
        service.Command = ToShellOrExecList(GetValue(map, "command"), "command", name);
        service.Entrypoint = ToShellOrExecList(GetValue(map, "entrypoint"), "entrypoint", name);
        service.Environment = ToKeyValues(GetValue(map, "environment"));
        service.EnvFile = ParseEnvFiles(GetValue(map, "env_file"), name);
        service.Ports = ParsePorts(GetValue(map, "ports"), name);
        service.Volumes = ParseServiceVolumes(GetValue(map, "volumes"), name);
        AppendServiceTmpfs(service.Volumes, GetValue(map, "tmpfs"), name);
        service.DependsOn = ParseDependsOn(GetValue(map, "depends_on"));
        service.HealthCheck = ParseHealthCheck(GetValue(map, "healthcheck"));
        ParseServiceNetworks(GetValue(map, "networks"), service, name);
        service.Labels = ToNonNullKeyValues(GetValue(map, "labels"));
        service.Annotations = ToNonNullKeyValues(GetValue(map, "annotations"));
        service.Secrets = ParseSecretAttachments(GetValue(map, "secrets"), name);

        if (!string.IsNullOrWhiteSpace(service.NetworkMode) && service.Networks.Count > 0)
            throw new ComposeLoadException($"service '{name}': 'network_mode' and 'networks' cannot be combined.");

        return service;
    }

    // Compose file — command / entrypoint: a string is shell form (/bin/sh -c), a list is exec form.

    /// <summary>
    /// Reads <c>command:</c> / <c>entrypoint:</c> in Compose exec form (YAML list → argv tokens) or
    /// shell form (YAML scalar → <c>/bin/sh -c "&lt;string&gt;"</c>). A map value is rejected.
    /// </summary>
    private static IList<string> ToShellOrExecList(object? value, string attribute, string serviceName)
    {
        if (value is null)
            return new List<string>();

        if (AsMap(value) is not null)
        {
            throw new ComposeLoadException(
                $"service '{serviceName}': '{attribute}' must be a string or a list of strings.");
        }

        // Scalars (including non-string YAML scalars) are Compose shell form.
        if (value is string s)
            return new List<string> { "/bin/sh", "-c", s };

        if (value is System.Collections.IEnumerable enumerable)
        {
            var tokens = new List<string>();
            foreach (var item in enumerable)
            {
                if (item is null)
                    continue;

                tokens.Add(Convert.ToString(item) ?? string.Empty);
            }

            return tokens;
        }

        return new List<string> { "/bin/sh", "-c", Convert.ToString(value) ?? string.Empty };
    }

    // Compose file — env_file: a string, a list of strings, or a list of { path, required } maps.

    /// <summary>
    /// Reads <c>env_file:</c> as a string, a list of strings, or a list of
    /// <c>{ path, required }</c> maps.
    /// </summary>
    private static IList<EnvFileSpec> ParseEnvFiles(object? value, string serviceName)
    {
        if (value is null)
            return new List<EnvFileSpec>();

        if (value is string path)
            return new List<EnvFileSpec> { new() { Path = path } };

        if (AsMap(value) is not null)
        {
            throw new ComposeLoadException(
                $"service '{serviceName}': 'env_file' must be a string or a list (got a map).");
        }

        var result = new List<EnvFileSpec>();
        foreach (var item in AsList(value))
        {
            if (item is not null)
                result.Add(ParseEnvFileEntry(item, serviceName));
        }

        return result;
    }

    /// <summary>
    /// Reads one <c>env_file</c> list entry: a bare path, or a <c>{ path, required }</c> map. Entries
    /// are required by default; <c>required: false</c> tolerates a missing file at run time.
    /// </summary>
    private static EnvFileSpec ParseEnvFileEntry(object item, string serviceName)
    {
        if (item is string path)
            return new EnvFileSpec { Path = path };

        if (AsMap(item) is not { } map)
        {
            throw new ComposeLoadException(
                $"service '{serviceName}': 'env_file' entries must be strings or {{ path, required }} maps.");
        }

        var entryPath = GetString(map, "path");
        if (string.IsNullOrWhiteSpace(entryPath))
        {
            throw new ComposeLoadException(
                $"service '{serviceName}': 'env_file' map entry requires a 'path'.");
        }

        return new EnvFileSpec
        {
            Path = entryPath,
            Required = GetBool(map, "required", defaultValue: true),
        };
    }

    // Compose file — ports: normalize short strings and long maps to CLI publish syntax.
    private static IList<string> ParsePorts(object? value, string serviceName)
    {
        var result = new List<string>();
        foreach (var item in AsList(value))
        {
            if (item is null)
                continue;

            if (AsMap(item) is not { } map)
            {
                result.Add(Convert.ToString(item) ?? string.Empty);
                continue;
            }

            var target = GetString(map, "target");
            if (string.IsNullOrWhiteSpace(target))
                throw new ComposeLoadException($"service '{serviceName}': long-form 'ports' entry requires 'target'.");

            var published = GetString(map, "published");
            var hostIp = GetString(map, "host_ip");
            var protocol = GetString(map, "protocol");
            var host = string.IsNullOrWhiteSpace(hostIp)
                ? string.Empty
                : hostIp!.Contains(':', StringComparison.Ordinal) ? $"[{hostIp}]:" : hostIp + ":";
            var mapping = string.IsNullOrWhiteSpace(published)
                ? target!
                : $"{host}{published}:{target}";

            if (!string.IsNullOrWhiteSpace(protocol) && !string.Equals(protocol, "tcp", StringComparison.OrdinalIgnoreCase))
                mapping += "/" + protocol!.ToLowerInvariant();

            result.Add(mapping);
        }

        return result;
    }

    // Compose file — volumes: short strings or long maps with type volume|bind|tmpfs.
    // Compose file — tmpfs: string or list of path / path:opts (appended as MountType.Tmpfs).

    /// <summary>
    /// Reads service <c>volumes:</c> as short-syntax strings and/or long-form maps.
    /// Supported long-form types are <c>volume</c>, <c>bind</c>, and <c>tmpfs</c>; anything else fails loudly.
    /// </summary>
    private static IList<ServiceMount> ParseServiceVolumes(object? value, string serviceName)
    {
        if (value is null)
            return new List<ServiceMount>();

        if (AsMap(value) is not null)
        {
            throw new ComposeLoadException(
                $"service '{serviceName}': 'volumes' must be a list (got a map).");
        }

        var result = new List<ServiceMount>();
        foreach (var item in AsList(value))
        {
            if (item is null)
                continue;

            if (AsMap(item) is { } map)
            {
                result.Add(ParseLongFormVolume(map, serviceName));
                continue;
            }

            var raw = Convert.ToString(item) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            result.Add(ServiceMount.FromShortSyntax(raw));
        }

        return result;
    }

    /// <summary>Reads one long-form <c>volumes:</c> map entry.</summary>
    private static ServiceMount ParseLongFormVolume(IDictionary<string, object?> map, string serviceName)
    {
        var typeRaw = GetString(map, "type");
        if (string.IsNullOrWhiteSpace(typeRaw))
        {
            throw new ComposeLoadException(
                $"service '{serviceName}': volume type is required (supported: volume, bind, tmpfs).");
        }

        var type = typeRaw.Trim();
        if (!string.Equals(type, "volume", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(type, "bind", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(type, "tmpfs", StringComparison.OrdinalIgnoreCase))
        {
            throw new ComposeLoadException(
                $"service '{serviceName}': volume type '{type}' is not supported (supported: volume, bind, tmpfs).");
        }

        var target = GetString(map, "target") ?? GetString(map, "destination");
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new ComposeLoadException(
                $"service '{serviceName}': volume entry with type '{type}' requires 'target'.");
        }

        // Compose file — volumes consistency: accepted and ignored on Linux.
        _ = GetString(map, "consistency");

        var readOnly = GetBool(map, "read_only");
        var source = GetString(map, "source");

        if (string.Equals(type, "tmpfs", StringComparison.OrdinalIgnoreCase))
        {
            var tmpfsOpts = AsMap(GetValue(map, "tmpfs"));
            return new ServiceMount
            {
                Type = MountType.Tmpfs,
                Target = target,
                ReadOnly = readOnly,
                TmpfsSize = tmpfsOpts is null ? null : GetString(tmpfsOpts, "size"),
                TmpfsMode = ServiceMount.NormalizeTmpfsMode(
                    tmpfsOpts is null ? null : GetString(tmpfsOpts, "mode")),
            };
        }

        if (string.Equals(type, "bind", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ComposeLoadException(
                    $"service '{serviceName}': volume type 'bind' requires 'source'.");
            }

            var bindOpts = AsMap(GetValue(map, "bind"));
            return new ServiceMount
            {
                Type = MountType.Bind,
                Source = source,
                Target = target,
                ReadOnly = readOnly,
                BindPropagation = bindOpts is null ? null : GetString(bindOpts, "propagation"),
                BindCreateHostPath = bindOpts is null ? null : GetOptionalBool(bindOpts, "create_host_path"),
                BindSelinux = bindOpts is null ? null : GetString(bindOpts, "selinux"),
                BindRecursive = bindOpts is null ? null : GetString(bindOpts, "recursive"),
            };
        }

        // type: volume
        var volumeOpts = AsMap(GetValue(map, "volume"));
        return new ServiceMount
        {
            Type = MountType.Volume,
            Source = source,
            Target = target,
            ReadOnly = readOnly,
            VolumeNocopy = volumeOpts is null ? null : GetOptionalBool(volumeOpts, "nocopy"),
            VolumeSubpath = volumeOpts is null ? null : GetString(volumeOpts, "subpath"),
        };
    }

    /// <summary>Appends service-level <c>tmpfs:</c> entries (string or list of strings) to <paramref name="mounts"/>.</summary>
    private static void AppendServiceTmpfs(IList<ServiceMount> mounts, object? value, string serviceName)
    {
        if (value is null)
            return;

        if (AsMap(value) is not null)
        {
            throw new ComposeLoadException(
                $"service '{serviceName}': 'tmpfs' must be a string or a list of strings.");
        }

        foreach (var item in AsList(value))
        {
            if (item is null)
                continue;

            if (AsMap(item) is not null)
            {
                throw new ComposeLoadException(
                    $"service '{serviceName}': 'tmpfs' entries must be strings (use volumes long form for tmpfs options maps).");
            }

            var raw = Convert.ToString(item) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            mounts.Add(ServiceMount.FromTmpfsShortSyntax(raw));
        }
    }

    // Compose file — depends_on: wait conditions come from the long map form; a required failure
    // aborts dependents.

    /// <summary>
    /// Reads <c>depends_on</c> in either the short list form (<c>[db]</c>, condition
    /// <c>service_started</c>) or the long map form (<c>db: { condition: service_healthy }</c>).
    /// A nameless entry is dropped, like a dependency on a service the document does not define.
    /// </summary>
    private static IList<ServiceDependency> ParseDependsOn(object? value)
    {
        var result = new List<ServiceDependency>();

        if (AsMap(value) is { } map)
        {
            foreach (var kvp in map)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                    continue;

                var entry = AsMap(kvp.Value);
                var condition = ParseCondition(entry is null ? null : GetString(entry, "condition"));
                var required = entry is null || GetBool(entry, "required", defaultValue: true);
                result.Add(new ServiceDependency(kvp.Key, condition, required));
            }

            return result;
        }

        foreach (var name in ToStringList(value))
        {
            if (!string.IsNullOrWhiteSpace(name))
                result.Add(new ServiceDependency(name));
        }

        return result;
    }

    private static DependencyCondition ParseCondition(string? condition) => condition switch
    {
        "service_healthy" => DependencyCondition.ServiceHealthy,
        "service_completed_successfully" => DependencyCondition.ServiceCompletedSuccessfully,
        _ => DependencyCondition.ServiceStarted,
    };

    // Compose file — healthcheck: disable: true or test: ["NONE"] turns the image's healthcheck off.

    /// <summary>
    /// Reads a service's <c>healthcheck:</c>. <c>disable: true</c> or a <c>["NONE"]</c> test is captured
    /// as <see cref="HealthCheckSpec.Disabled"/>; the string short form is stored as a single test token.
    /// </summary>
    private static HealthCheckSpec? ParseHealthCheck(object? value)
    {
        if (AsMap(value) is not { } map)
            return null;

        var test = ToStringList(GetValue(map, "test"));
        var disabled = GetBool(map, "disable")
            || (test.Count > 0 && string.Equals(test[0], "NONE", StringComparison.Ordinal));

        return new HealthCheckSpec
        {
            Disabled = disabled,
            Test = test,
            Interval = GetString(map, "interval"),
            Timeout = GetString(map, "timeout"),
            Retries = GetInt(map, "retries"),
            StartPeriod = GetString(map, "start_period"),
        };
    }

    private static void ParseServiceNetworks(object? value, ServiceSpec service, string serviceName)
    {
        if (AsMap(value) is not { } networks)
        {
            service.Networks = ToStringList(value);
            return;
        }

        foreach (var entry in networks)
        {
            service.Networks.Add(entry.Key);
            if (entry.Value is null)
                continue;

            if (AsMap(entry.Value) is not { } options)
            {
                throw new ComposeLoadException(
                    $"service '{serviceName}': network '{entry.Key}' options must be a mapping.");
            }

            service.NetworkAttachments[entry.Key] = new ServiceNetworkAttachment
            {
                Aliases = ToStringList(GetValue(options, "aliases")),
                IPv4Address = GetString(options, "ipv4_address"),
            };
        }
    }

    private static IDictionary<string, string> ParseUlimits(object? value)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (AsMap(value) is not { } map)
            return result;

        foreach (var entry in map)
        {
            if (AsMap(entry.Value) is { } limits)
            {
                var soft = GetString(limits, "soft");
                var hard = GetString(limits, "hard");
                if (!string.IsNullOrWhiteSpace(soft) || !string.IsNullOrWhiteSpace(hard))
                    result[entry.Key] = $"{soft ?? hard}:{hard ?? soft}";
            }
            else if (entry.Value is not null)
            {
                result[entry.Key] = Convert.ToString(entry.Value) ?? string.Empty;
            }
        }

        return result;
    }

    // Compose file — build: the string short form is the context; the map form adds supported CLI options.
    private static BuildSpec? ParseBuild(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string context:
                return new BuildSpec { Context = context };
            default:
                var map = AsMap(value);
                if (map is null)
                    return null;

                return new BuildSpec
                {
                    Context = GetString(map, "context"),
                    Dockerfile = GetString(map, "dockerfile"),
                    Target = GetString(map, "target"),
                    Args = ToKeyValues(GetValue(map, "args")),
                    Labels = ToNonNullKeyValues(GetValue(map, "labels")),
                    NoCache = GetBool(map, "no_cache"),
                    Pull = GetBool(map, "pull"),
                    Secrets = ParseBuildSecrets(GetValue(map, "secrets")),
                };
        }
    }

    private static IList<string> ParseBuildSecrets(object? value)
    {
        var result = new List<string>();
        foreach (var item in AsList(value))
        {
            if (item is null)
                continue;

            if (AsMap(item) is not { } map)
            {
                result.Add(Convert.ToString(item) ?? string.Empty);
                continue;
            }

            var source = GetString(map, "source");
            var target = GetString(map, "target") ?? source;
            if (!string.IsNullOrWhiteSpace(source))
                result.Add($"id={target},src={source}");
        }

        return result;
    }

    // Compose file — networks / volumes (top level): external: true means "already exists, do not create".
    private static NetworkSpec ParseNetwork(string name, object? value)
    {
        var map = AsMap(value);
        var result = new NetworkSpec
        {
            Name = name,
            Driver = map is null ? null : GetString(map, "driver"),
            DriverOptions = map is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : ToNonNullKeyValues(GetValue(map, "driver_opts")),
            Internal = map is not null && GetBool(map, "internal"),
            External = map is not null && GetBool(map, "external"),
        };

        if (map is not null
            && AsMap(GetValue(map, "ipam")) is { } ipam
            && AsList(GetValue(ipam, "config")).FirstOrDefault() is { } first
            && AsMap(first) is { } config)
        {
            result.Subnet = GetString(config, "subnet");
            result.Gateway = GetString(config, "gateway");
            result.IpRange = GetString(config, "ip_range");
        }

        return result;
    }

    private static VolumeSpec ParseVolume(string name, object? value)
    {
        var map = AsMap(value);
        return new VolumeSpec
        {
            Name = name,
            Driver = map is null ? null : GetString(map, "driver"),
            DriverOptions = map is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : ToNonNullKeyValues(GetValue(map, "driver_opts")),
            External = map is not null && GetBool(map, "external"),
        };
    }

    /// <summary>
    /// Compose file — secrets (top level): <c>file:</c> or <c>environment:</c>. A scalar is treated as
    /// a file path. <c>external: true</c> is rejected (no Swarm secret store).
    /// </summary>
    private static SecretSpec ParseSecret(string name, object? value)
    {
        if (value is string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ComposeLoadException($"secret '{name}': 'file' path is empty.");

            return new SecretSpec { Name = name, File = filePath };
        }

        var map = AsMap(value);
        if (map is null)
        {
            throw new ComposeLoadException(
                $"secret '{name}' must declare 'file' or 'environment' (a mapping, or a file path string).");
        }

        if (IsExternal(map))
        {
            throw new ComposeLoadException(
                $"secret '{name}': 'external: true' is not supported (no Swarm secret store). Use 'file:' or 'environment:'.");
        }

        var file = GetString(map, "file");
        var environment = GetString(map, "environment");
        var hasFile = !string.IsNullOrWhiteSpace(file);
        var hasEnvironment = !string.IsNullOrWhiteSpace(environment);
        if (hasFile == hasEnvironment)
        {
            throw new ComposeLoadException(
                $"secret '{name}' must declare exactly one of 'file' or 'environment'.");
        }

        return new SecretSpec { Name = name, File = hasFile ? file : null, Environment = hasEnvironment ? environment : null };
    }

    /// <summary>Compose file — secrets (service): a list of names or <c>{ source, target }</c> maps.</summary>
    private static IList<SecretAttachment> ParseSecretAttachments(object? value, string serviceName)
    {
        var result = new List<SecretAttachment>();
        if (value is null)
            return result;

        foreach (var item in AsList(value))
        {
            if (item is string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                    throw new ComposeLoadException($"service '{serviceName}': 'secrets' entry is empty.");

                result.Add(new SecretAttachment { Source = name, Target = DefaultSecretTarget(name) });
                continue;
            }

            var map = AsMap(item);
            if (map is null)
            {
                throw new ComposeLoadException(
                    $"service '{serviceName}': 'secrets' entries must be a name or a mapping with 'source'.");
            }

            var source = GetString(map, "source");
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ComposeLoadException(
                    $"service '{serviceName}': long-form 'secrets' entry must specify 'source'.");
            }

            var target = GetString(map, "target");
            result.Add(new SecretAttachment
            {
                Source = source,
                Target = string.IsNullOrWhiteSpace(target) ? DefaultSecretTarget(source) : target,
            });
        }

        return result;
    }

    private static void ValidateSecretAttachments(ComposeFile file)
    {
        foreach (var service in file.Services.Values)
        {
            foreach (var attachment in service.Secrets)
            {
                if (!file.Secrets.ContainsKey(attachment.Source))
                {
                    throw new ComposeLoadException(
                        $"service '{service.Name}': secret '{attachment.Source}' is not declared in top-level 'secrets'.");
                }
            }
        }
    }

    private static string DefaultSecretTarget(string source) => "/run/secrets/" + source;

    private static bool IsExternal(IDictionary<string, object?> map)
        => GetBool(map, "external") || AsMap(GetValue(map, "external")) is not null;

    // --- YAML graph helpers -------------------------------------------------

    private static IDictionary<string, object?>? AsMap(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case IDictionary<string, object?> typed:
                return typed;
            case IDictionary<object, object?> raw:
                var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var kvp in raw)
                    normalized[Convert.ToString(kvp.Key) ?? string.Empty] = kvp.Value;

                return normalized;
            default:
                return null;
        }
    }

    private static IList<object?> AsList(object? value)
    {
        switch (value)
        {
            case null:
                return new List<object?>();
            case string s:
                return new List<object?> { s };
            case System.Collections.IEnumerable enumerable:
                var list = new List<object?>();
                foreach (var item in enumerable)
                    list.Add(item);

                return list;
            default:
                return new List<object?> { value };
        }
    }

    private static object? GetValue(IDictionary<string, object?> map, string key)
        => map.TryGetValue(key, out var value) ? value : null;

    private static string? GetString(IDictionary<string, object?> map, string key)
        => GetValue(map, key) is { } value ? Convert.ToString(value) : null;

    private static bool GetBool(IDictionary<string, object?> map, string key, bool defaultValue = false)
        => GetValue(map, key) is { } value && bool.TryParse(Convert.ToString(value), out var b) ? b : defaultValue;

    /// <summary>Reads a bool when the key is present; otherwise <c>null</c>.</summary>
    private static bool? GetOptionalBool(IDictionary<string, object?> map, string key)
    {
        if (GetValue(map, key) is not { } value)
            return null;

        return bool.TryParse(Convert.ToString(value), out var b) ? b : null;
    }

    private static int? GetInt(IDictionary<string, object?> map, string key)
        => GetValue(map, key) is { } value
            && int.TryParse(Convert.ToString(value), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? n
            : (int?)null;

    private static IList<string> ToStringList(object? value)
        => AsList(value)
            .Where(v => v is not null)
            .Select(v => Convert.ToString(v) ?? string.Empty)
            .ToList();

    private static string? ScalarOrJoined(object? value)
    {
        if (value is null)
            return null;

        if (value is string)
            return Convert.ToString(value);

        if (AsMap(value) is not null)
            return null;

        return string.Join(",", ToStringList(value));
    }

    /// <summary>Keys of a map, or items of a list (used by depends_on / networks).</summary>
    private static IList<string> ToKeyList(object? value)
    {
        if (AsMap(value) is { } map)
            return map.Keys.ToList();

        return ToStringList(value);
    }

    /// <summary>Reads a map or a list of "KEY=VALUE" entries into a dictionary (nullable values).</summary>
    private static IDictionary<string, string?> ToKeyValues(object? value)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (AsMap(value) is { } map)
        {
            foreach (var kvp in map)
                result[kvp.Key] = kvp.Value is null ? null : Convert.ToString(kvp.Value);

            return result;
        }

        foreach (var item in ToStringList(value))
        {
            var index = item.IndexOf('=');
            if (index < 0)
                result[item] = null;
            else
                result[item.Substring(0, index)] = item.Substring(index + 1);
        }

        return result;
    }

    private static IDictionary<string, string> ToNonNullKeyValues(object? value)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kvp in ToKeyValues(value))
            result[kvp.Key] = kvp.Value ?? string.Empty;

        return result;
    }
}

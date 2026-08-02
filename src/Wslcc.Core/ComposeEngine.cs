using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Wslcc.Abstractions;
using Wslcc.Abstractions.Compose;

namespace Wslcc.Core;

/// <summary>
/// Default <see cref="IComposeEngine"/> implementation over a set of registered providers.
/// </summary>
public sealed class ComposeEngine : IComposeEngine
{
    private readonly IReadOnlyList<IContainerProvider> _providers;
    private readonly string? _defaultProvider;

    public ComposeEngine(IEnumerable<IContainerProvider> providers, string? defaultProvider = null)
    {
        _providers = providers?.ToList() ?? throw new ArgumentNullException(nameof(providers));
        _defaultProvider = string.IsNullOrWhiteSpace(defaultProvider) ? null : defaultProvider;
    }

    public IReadOnlyList<string> ProviderNames => _providers.Select(p => p.Name).ToList();

    public async Task<IReadOnlyList<ProviderInfo>> GetProviderInfosAsync(CancellationToken cancellationToken = default)
    {
        var tasks = _providers.Select(p => p.GetProviderInfoAsync(cancellationToken));
        var infos = await Task.WhenAll(tasks).ConfigureAwait(false);
        return infos;
    }

    public Task<ProviderInfo> GetProviderInfoAsync(string? providerName, CancellationToken cancellationToken = default)
        => ResolveProvider(providerName).GetProviderInfoAsync(cancellationToken);

    public async Task<IReadOnlyList<ServiceOperationResult>> UpAsync(
        string projectName,
        ComposeFile file,
        string? providerName,
        bool pull,
        BuildPolicy buildPolicy,
        string? baseDirectory,
        IReadOnlyDictionary<string, string>? serviceConfigHashes = null,
        CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(providerName);
        var results = new List<ServiceOperationResult>();
        var startedContainers = new Dictionary<string, string>(StringComparer.Ordinal);
        var failed = new HashSet<string>(StringComparer.Ordinal);

        var existingByService = await ListByServiceAsync(provider, projectName, cancellationToken).ConfigureAwait(false);

        // Create the project's networks and named volumes up front so every service can attach to them.
        await ProvisionResourcesAsync(provider, projectName, file, cancellationToken).ConfigureAwait(false);

        // --pull / --build ask for fresh images, so containers are always recreated to pick them up;
        // otherwise recreation is driven by the per-service config hash below.
        var forceRecreate = pull || buildPolicy == BuildPolicy.Always;

        foreach (var service in OrderServices(file))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A service whose required dependency did not come up is not started (docker-compose aborts
            // dependents), and it in turn fails its own dependents.
            var brokenDependency = service.DependsOn.FirstOrDefault(d => d.Required && failed.Contains(d.Name));
            if (brokenDependency is not null)
            {
                results.Add(new ServiceOperationResult(
                    service.Name, "failed", Error: $"dependency '{brokenDependency.Name}' failed to start"));
                failed.Add(service.Name);
                continue;
            }

            var existing = existingByService.GetValueOrDefault(service.Name);
            var configHash = serviceConfigHashes?.GetValueOrDefault(service.Name);

            // Leave an unchanged, still-running container in place instead of recreating it.
            if (!forceRecreate
                && existing is not null
                && IsRunning(existing)
                && configHash is { Length: > 0 }
                && string.Equals(existing.ConfigHash, configHash, StringComparison.Ordinal))
            {
                startedContainers[service.Name] = existing.Name;
                results.Add(new ServiceOperationResult(service.Name, "running", existing.Id));
                continue;
            }

            try
            {
                await WaitForDependenciesAsync(provider, projectName, file, service, startedContainers, cancellationToken)
                    .ConfigureAwait(false);

                var image = await PrepareServiceImageAsync(provider, projectName, service, baseDirectory, pull, buildPolicy, cancellationToken)
                    .ConfigureAwait(false);

                var spec = ToRunSpec(projectName, service, image);
                if (configHash is { Length: > 0 })
                {
                    spec.Labels[WslccLabels.ConfigHash] = configHash;
                }

                var networks = ResolveServiceNetworks(file, projectName, service);
                if (networks.Count > 0)
                {
                    // The container is created on its first network (with the service name as an alias);
                    // any additional networks are connected once it is running.
                    spec.Network = networks[0];
                    spec.NetworkAlias = service.Name;
                }

                foreach (var mount in service.Volumes)
                {
                    spec.Volumes.Add(ResolveMount(file, projectName, baseDirectory, mount));
                }

                await TryRemoveExistingAsync(provider, spec.Name, cancellationToken).ConfigureAwait(false);

                var id = await provider.RunContainerAsync(spec, cancellationToken).ConfigureAwait(false);

                for (var i = 1; i < networks.Count; i++)
                {
                    await provider.ConnectNetworkAsync(networks[i], spec.Name, service.Name, cancellationToken).ConfigureAwait(false);
                }

                startedContainers[service.Name] = spec.Name;
                results.Add(new ServiceOperationResult(service.Name, "started", id));
            }
            catch (ProviderException ex)
            {
                failed.Add(service.Name);
                results.Add(new ServiceOperationResult(service.Name, "failed", Error: ex.Message));
            }
        }

        return results;
    }

    private static bool IsRunning(ContainerInfo container)
        => string.Equals(container.State, "running", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lists the project's existing containers indexed by service name (first wins on duplicates).</summary>
    private static async Task<Dictionary<string, ContainerInfo>> ListByServiceAsync(
        IContainerProvider provider,
        string projectName,
        CancellationToken cancellationToken)
    {
        var containers = await provider.ListContainersAsync(projectName, all: true, cancellationToken).ConfigureAwait(false);
        var byService = new Dictionary<string, ContainerInfo>(StringComparer.Ordinal);
        foreach (var container in containers)
        {
            if (container.Service is { } service && !byService.ContainsKey(service))
            {
                byService[service] = container;
            }
        }

        return byService;
    }

    private static readonly TimeSpan DependencyPollInterval = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan DependencyWaitTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Blocks until every <c>depends_on</c> condition of <paramref name="service"/> is satisfied.
    /// <see cref="DependencyCondition.ServiceStarted"/> is a no-op (start-order is already guaranteed by
    /// <see cref="OrderServices"/>); <see cref="DependencyCondition.ServiceHealthy"/> waits for a healthy
    /// healthcheck; <see cref="DependencyCondition.ServiceCompletedSuccessfully"/> waits for a clean
    /// exit. Unknown dependencies are ignored (as in ordering). Throws <see cref="ProviderException"/>
    /// when a condition can never be met (unhealthy, non-zero exit, or no healthcheck for
    /// <c>service_healthy</c>).
    /// </summary>
    private static async Task WaitForDependenciesAsync(
        IContainerProvider provider,
        string projectName,
        ComposeFile file,
        ServiceSpec service,
        IReadOnlyDictionary<string, string> startedContainers,
        CancellationToken cancellationToken)
    {
        foreach (var dependency in service.DependsOn)
        {
            if (dependency.Condition == DependencyCondition.ServiceStarted
                || !file.Services.TryGetValue(dependency.Name, out var dependencyService))
            {
                continue;
            }

            var container = startedContainers.TryGetValue(dependency.Name, out var name)
                ? name
                : WslccLabels.ContainerName(projectName, dependency.Name);

            var hasHealthCheck = dependencyService.HealthCheck is { Disabled: false };

            await WaitForConditionAsync(provider, container, dependency, hasHealthCheck, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task WaitForConditionAsync(
        IContainerProvider provider,
        string container,
        ServiceDependency dependency,
        bool dependencyHasHealthCheck,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + DependencyWaitTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await provider.GetContainerStateAsync(container, cancellationToken).ConfigureAwait(false);

            if (IsConditionSatisfied(dependency, state, dependencyHasHealthCheck))
            {
                return;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new ProviderException(
                    $"timed out waiting for dependency '{dependency.Name}' to become '{Describe(dependency.Condition)}'");
            }

            await Task.Delay(DependencyPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns whether a dependency's condition is met given its current <paramref name="state"/>. A
    /// <c>false</c> result means "not yet, keep polling"; a condition that can never be met throws.
    /// </summary>
    private static bool IsConditionSatisfied(ServiceDependency dependency, ContainerRuntimeState? state, bool dependencyHasHealthCheck)
    {
        switch (dependency.Condition)
        {
            case DependencyCondition.ServiceHealthy:
                return state?.Health switch
                {
                    HealthStatus.Healthy => true,
                    HealthStatus.Unhealthy => throw new ProviderException($"dependency '{dependency.Name}' is unhealthy"),
                    HealthStatus.None when state is not null && !dependencyHasHealthCheck => throw new ProviderException(
                        $"dependency '{dependency.Name}' has no healthcheck; cannot satisfy condition service_healthy"),
                    _ => false,
                };

            case DependencyCondition.ServiceCompletedSuccessfully:
                if (state is null || !state.HasExited)
                {
                    return false;
                }

                return state.ExitCode is 0
                    ? true
                    : throw new ProviderException(
                        $"dependency '{dependency.Name}' did not complete successfully (exit code {state.ExitCode})");

            default:
                return true;
        }
    }

    private static string Describe(DependencyCondition condition) => condition switch
    {
        DependencyCondition.ServiceHealthy => "healthy",
        DependencyCondition.ServiceCompletedSuccessfully => "completed successfully",
        _ => "started",
    };

    /// <summary>
    /// Resolves the image to run for a service and makes sure it is available. A <c>build:</c> service is
    /// built (tagged as its <c>image:</c> or <c>&lt;project&gt;-&lt;service&gt;</c>) according to
    /// <paramref name="buildPolicy"/>: <see cref="BuildPolicy.Always"/> rebuilds every time,
    /// <see cref="BuildPolicy.Never"/> fails when the image is missing, and <see cref="BuildPolicy.Auto"/>
    /// builds only when it is missing (matching docker-compose's default <c>up</c>). A service that only
    /// references an image has it pulled if missing (or always when <paramref name="pull"/> is set).
    /// Returns the image the container should run; throws <see cref="ProviderException"/> when nothing
    /// runnable is defined.
    /// </summary>
    private static async Task<string> PrepareServiceImageAsync(
        IContainerProvider provider,
        string projectName,
        ServiceSpec service,
        string? baseDirectory,
        bool pull,
        BuildPolicy buildPolicy,
        CancellationToken cancellationToken)
    {
        if (service.Build is not null)
        {
            var (spec, error) = CreateBuildSpec(projectName, service, baseDirectory);
            if (spec is null)
            {
                throw new ProviderException(error!);
            }

            switch (buildPolicy)
            {
                case BuildPolicy.Always:
                    await provider.BuildImageAsync(spec, cancellationToken).ConfigureAwait(false);
                    break;

                case BuildPolicy.Never:
                    if (!await provider.ImageExistsAsync(spec.Tag, cancellationToken).ConfigureAwait(false))
                    {
                        throw new ProviderException(
                            $"image '{spec.Tag}' is not present and --no-build was set; run 'wslcc compose build' first");
                    }

                    break;

                default:
                    if (!await provider.ImageExistsAsync(spec.Tag, cancellationToken).ConfigureAwait(false))
                    {
                        await provider.BuildImageAsync(spec, cancellationToken).ConfigureAwait(false);
                    }

                    break;
            }

            return spec.Tag;
        }

        if (string.IsNullOrWhiteSpace(service.Image))
        {
            throw new ProviderException("no 'image' or 'build:' section specified");
        }

        await provider.EnsureImageAsync(service.Image!, pull, cancellationToken).ConfigureAwait(false);
        return service.Image!;
    }

    public async Task<IReadOnlyList<ServiceOperationResult>> DownAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        bool removeVolumes = false,
        CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(providerName);
        var containers = await provider.ListContainersAsync(projectName, all: true, cancellationToken).ConfigureAwait(false);

        // Tear down in reverse dependency order (dependents first), mirroring `stop`.
        containers = OrderContainers(file, containers, reverse: true);

        var results = new List<ServiceOperationResult>();
        foreach (var container in containers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var serviceName = container.Service ?? container.Name;

            try
            {
                await provider.StopContainerAsync(container.Name, cancellationToken).ConfigureAwait(false);
            }
            catch (ProviderException)
            {
                // Best-effort stop; still attempt removal.
            }

            try
            {
                await provider.RemoveContainerAsync(container.Name, force: true, cancellationToken).ConfigureAwait(false);
                results.Add(new ServiceOperationResult(serviceName, "removed", container.Id));
            }
            catch (ProviderException ex)
            {
                results.Add(new ServiceOperationResult(serviceName, "failed", container.Id, ex.Message));
            }
        }

        // Remove the networks wslcc created for the project (once their containers are gone). Named
        // volumes are kept unless explicitly requested, matching `docker compose down` (data is precious).
        await RemoveProjectResourcesAsync(provider, projectName, removeVolumes, results, cancellationToken)
            .ConfigureAwait(false);

        return results;
    }

    /// <summary>
    /// Best-effort teardown of the project's networks (always) and named volumes (only when requested).
    /// Both are discovered by their <c>wslcc.project</c> label, so external resources — which wslcc never
    /// labelled — are left untouched. Listing failures are swallowed so container removal still reports.
    /// </summary>
    private static async Task RemoveProjectResourcesAsync(
        IContainerProvider provider,
        string projectName,
        bool removeVolumes,
        List<ServiceOperationResult> results,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> networks;
        try
        {
            networks = await provider.ListNetworkNamesAsync(projectName, cancellationToken).ConfigureAwait(false);
        }
        catch (ProviderException)
        {
            networks = Array.Empty<string>();
        }

        foreach (var network in networks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await provider.RemoveNetworkAsync(network, cancellationToken).ConfigureAwait(false);
                results.Add(new ServiceOperationResult($"network {network}", "removed"));
            }
            catch (ProviderException ex)
            {
                results.Add(new ServiceOperationResult($"network {network}", "failed", Error: ex.Message));
            }
        }

        if (!removeVolumes)
        {
            return;
        }

        IReadOnlyList<string> volumes;
        try
        {
            volumes = await provider.ListVolumeNamesAsync(projectName, cancellationToken).ConfigureAwait(false);
        }
        catch (ProviderException)
        {
            volumes = Array.Empty<string>();
        }

        foreach (var volume in volumes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await provider.RemoveVolumeAsync(volume, cancellationToken).ConfigureAwait(false);
                results.Add(new ServiceOperationResult($"volume {volume}", "removed"));
            }
            catch (ProviderException ex)
            {
                results.Add(new ServiceOperationResult($"volume {volume}", "failed", Error: ex.Message));
            }
        }
    }

    public Task<IReadOnlyList<ContainerInfo>> PsAsync(
        string? projectName,
        string? providerName,
        bool all,
        CancellationToken cancellationToken = default)
        => ResolveProvider(providerName).ListContainersAsync(projectName, all, cancellationToken);

    public Task<IReadOnlyList<ServiceOperationResult>> StartAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        CancellationToken cancellationToken = default)
        => ApplyToContainersAsync(
            projectName, file, providerName, services, "started", reverseOrder: false,
            (provider, containerName, ct) => provider.StartContainerAsync(containerName, ct),
            cancellationToken);

    public Task<IReadOnlyList<ServiceOperationResult>> StopAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        CancellationToken cancellationToken = default)
        => ApplyToContainersAsync(
            projectName, file, providerName, services, "stopped", reverseOrder: true,
            (provider, containerName, ct) => provider.StopContainerAsync(containerName, ct),
            cancellationToken);

    public Task<IReadOnlyList<ServiceOperationResult>> RestartAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        CancellationToken cancellationToken = default)
        => ApplyToContainersAsync(
            projectName, file, providerName, services, "restarted", reverseOrder: false,
            (provider, containerName, ct) => provider.RestartContainerAsync(containerName, ct),
            cancellationToken);

    public async Task<IReadOnlyList<ServiceOperationResult>> PullAsync(
        ComposeFile file,
        string? providerName,
        IReadOnlyList<string>? services,
        CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(providerName);
        var results = new List<ServiceOperationResult>();

        foreach (var service in SelectServices(file, services))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Build-only services have nothing to pull (matching `docker compose pull` and `BuildAsync`).
            if (string.IsNullOrWhiteSpace(service.Image))
            {
                continue;
            }

            try
            {
                await provider.EnsureImageAsync(service.Image!, alwaysPull: true, cancellationToken).ConfigureAwait(false);
                results.Add(new ServiceOperationResult(service.Name, "pulled"));
            }
            catch (ProviderException ex)
            {
                results.Add(new ServiceOperationResult(service.Name, "failed", Error: ex.Message));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<ServiceOperationResult>> BuildAsync(
        string projectName,
        ComposeFile file,
        string? providerName,
        string? baseDirectory,
        IReadOnlyList<string>? services,
        CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(providerName);
        var results = new List<ServiceOperationResult>();

        foreach (var service in SelectServices(file, services))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (service.Build is null)
            {
                continue; // nothing to build for services that only reference a pre-built image
            }

            var (spec, error) = CreateBuildSpec(projectName, service, baseDirectory);
            if (spec is null)
            {
                results.Add(new ServiceOperationResult(service.Name, "failed", Error: error));
                continue;
            }

            try
            {
                await provider.BuildImageAsync(spec, cancellationToken).ConfigureAwait(false);
                results.Add(new ServiceOperationResult(service.Name, "built"));
            }
            catch (ProviderException ex)
            {
                results.Add(new ServiceOperationResult(service.Name, "failed", Error: ex.Message));
            }
        }

        return results;
    }

    /// <summary>
    /// Builds the <see cref="ImageBuildSpec"/> for a service's <c>build:</c> section, resolving the build
    /// context against <paramref name="baseDirectory"/> and tagging the image as the service's
    /// <c>image:</c> (or <c>&lt;project&gt;-&lt;service&gt;</c> when none is given). Returns a
    /// <c>null</c> spec with an error message when the build section has no usable context.
    /// </summary>
    private static (ImageBuildSpec? Spec, string? Error) CreateBuildSpec(
        string projectName,
        ServiceSpec service,
        string? baseDirectory)
    {
        var context = ResolveBuildContext(service.Build!.Context, baseDirectory);
        if (context is null)
        {
            return (null, "'build' has no context");
        }

        var spec = new ImageBuildSpec
        {
            Context = context,
            Dockerfile = service.Build.Dockerfile,
            Target = service.Build.Target,
            Tag = string.IsNullOrWhiteSpace(service.Image)
                ? WslccLabels.ContainerName(projectName, service.Name)
                : service.Image!,
        };

        foreach (var arg in service.Build.Args)
        {
            spec.Args[arg.Key] = arg.Value;
        }

        return (spec, null);
    }

    /// <summary>
    /// Resolves a (possibly relative) build context against the compose file's directory. Falls back
    /// to the context as-is when no base directory is known (best effort; resolved by the provider CLI
    /// against its own working directory).
    /// </summary>
    private static string? ResolveBuildContext(string? context, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return null;
        }

        if (Path.IsPathRooted(context) || string.IsNullOrWhiteSpace(baseDirectory))
        {
            return context;
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, context));
    }

    /// <summary>
    /// Ensures the project's networks and named volumes exist before any container is started. Declared
    /// networks/volumes are created project-prefixed and labelled (so <c>down</c> can find them); those
    /// marked <c>external: true</c> are assumed to exist and are left alone. The implicit
    /// <c>&lt;project&gt;_default</c> network is created whenever a service declares no networks of its own.
    /// </summary>
    private static async Task ProvisionResourcesAsync(
        IContainerProvider provider,
        string projectName,
        ComposeFile file,
        CancellationToken cancellationToken)
    {
        foreach (var kvp in file.Volumes)
        {
            if (kvp.Value.External)
            {
                continue;
            }

            var spec = new VolumeCreateSpec { Name = WslccLabels.VolumeName(projectName, kvp.Key), Driver = kvp.Value.Driver };
            spec.Labels[WslccLabels.Project] = projectName;
            spec.Labels[WslccLabels.Volume] = kvp.Key;
            await provider.EnsureVolumeAsync(spec, cancellationToken).ConfigureAwait(false);
        }

        foreach (var kvp in file.Networks)
        {
            if (kvp.Value.External)
            {
                continue;
            }

            var spec = new NetworkCreateSpec { Name = WslccLabels.NetworkName(projectName, kvp.Key), Driver = kvp.Value.Driver };
            spec.Labels[WslccLabels.Project] = projectName;
            spec.Labels[WslccLabels.Network] = kvp.Key;
            await provider.EnsureNetworkAsync(spec, cancellationToken).ConfigureAwait(false);
        }

        if (file.Services.Values.Any(s => s.Networks.Count == 0))
        {
            var spec = new NetworkCreateSpec { Name = WslccLabels.DefaultNetworkName(projectName) };
            spec.Labels[WslccLabels.Project] = projectName;
            spec.Labels[WslccLabels.Network] = "default";
            await provider.EnsureNetworkAsync(spec, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The networks a service attaches to: the ones it lists (mapped to their project-prefixed names, or
    /// their bare name when the network is <c>external</c>), or the implicit default network when it
    /// lists none.
    /// </summary>
    private static IReadOnlyList<string> ResolveServiceNetworks(ComposeFile file, string projectName, ServiceSpec service)
    {
        if (service.Networks.Count == 0)
        {
            return new[] { WslccLabels.DefaultNetworkName(projectName) };
        }

        return service.Networks
            .Select(key => file.Networks.TryGetValue(key, out var spec) && spec.External
                ? key
                : WslccLabels.NetworkName(projectName, key))
            .ToList();
    }

    /// <summary>
    /// Resolves a service volume in Compose short syntax (<c>[SOURCE:]TARGET[:MODE]</c>) into a
    /// <c>docker run -v</c> value: named-volume sources are mapped to their project-prefixed name (unless
    /// external or undeclared), relative bind sources are resolved against the project directory, and
    /// anonymous volumes (<c>TARGET</c> only) pass through unchanged.
    /// </summary>
    private static string ResolveMount(ComposeFile file, string projectName, string? baseDirectory, string raw)
    {
        var (source, target, mode) = SplitMount(raw);
        if (source is null || target is null)
        {
            return raw;
        }

        var resolvedSource = IsBindSource(source)
            ? ResolveBindSource(source, baseDirectory)
            : ResolveVolumeSource(file, projectName, source);

        return string.IsNullOrEmpty(mode) ? $"{resolvedSource}:{target}" : $"{resolvedSource}:{target}:{mode}";
    }

    /// <summary>
    /// Splits a mount string into source / target / mode. A single segment is an anonymous volume
    /// (target only). A leading Windows drive letter (e.g. <c>C:\path</c>) is kept with its source rather
    /// than treated as a separator.
    /// </summary>
    private static (string? Source, string? Target, string? Mode) SplitMount(string raw)
    {
        var value = raw.Trim();
        if (value.Length == 0)
        {
            return (null, null, null);
        }

        var parts = value.Split(':');

        // Re-join a Windows drive ("C" + "\path") that ':' split apart.
        if (parts.Length >= 2 && parts[0].Length == 1 && char.IsLetter(parts[0][0]))
        {
            parts = new[] { parts[0] + ":" + parts[1] }.Concat(parts.Skip(2)).ToArray();
        }

        return parts.Length switch
        {
            1 => (null, parts[0], null),
            2 => (parts[0], parts[1], null),
            3 => (parts[0], parts[1], parts[2]),
            _ => (null, null, null), // malformed; caller passes the original through
        };
    }

    private static bool IsBindSource(string source)
        => source.StartsWith('/')
            || source.StartsWith('.')
            || source.StartsWith('~')
            || source.Contains('/')
            || source.Contains('\\')
            || (source.Length >= 2 && char.IsLetter(source[0]) && source[1] == ':');

    private static string ResolveBindSource(string source, string? baseDirectory)
    {
        if (Path.IsPathRooted(source) || source.StartsWith('~') || string.IsNullOrWhiteSpace(baseDirectory))
        {
            return source;
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, source));
    }

    private static string ResolveVolumeSource(ComposeFile file, string projectName, string source)
    {
        if (file.Volumes.TryGetValue(source, out var spec))
        {
            return spec.External ? source : WslccLabels.VolumeName(projectName, source);
        }

        // Undeclared named volume: pass through (the runtime creates it implicitly, unscoped to wslcc).
        return source;
    }

    /// <summary>
    /// Selects the requested services from the file, or every service when none were requested. A
    /// requested name the file does not define is rejected (instead of being silently ignored).
    /// </summary>
    private static IEnumerable<ServiceSpec> SelectServices(ComposeFile file, IReadOnlyList<string>? services)
    {
        if (services is not { Count: > 0 })
        {
            return file.Services.Values;
        }

        var selected = new List<ServiceSpec>();
        var unknown = new List<string>();
        foreach (var name in services)
        {
            if (file.Services.TryGetValue(name, out var service))
            {
                selected.Add(service);
            }
            else
            {
                unknown.Add(name);
            }
        }

        ThrowIfUnknownServices(unknown);
        return selected;
    }

    /// <summary>Throws a <see cref="ProviderException"/> listing any service names that were not found.</summary>
    private static void ThrowIfUnknownServices(IReadOnlyList<string> unknown)
    {
        if (unknown.Count > 0)
        {
            throw new ProviderException($"no such service: {string.Join(", ", unknown)}");
        }
    }

    /// <summary>
    /// Orders the project's existing containers by the compose <c>depends_on</c> graph (dependencies
    /// first), optionally reversed for teardown-style operations. Containers whose service is not in the
    /// file keep their original relative order after the known ones. When no file is provided there is no
    /// dependency graph, so the provider's listing order is preserved as-is.
    /// </summary>
    private static IReadOnlyList<ContainerInfo> OrderContainers(
        ComposeFile? file,
        IReadOnlyList<ContainerInfo> containers,
        bool reverse)
    {
        if (file is null)
        {
            return containers;
        }

        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        var next = 0;
        foreach (var service in OrderServices(file))
        {
            rank[service.Name] = next++;
        }

        int RankOf(ContainerInfo c)
            => c.Service is not null && rank.TryGetValue(c.Service, out var r) ? r : int.MaxValue;

        var ordered = containers
            .Select((container, index) => (container, index))
            .OrderBy(t => RankOf(t.container))
            .ThenBy(t => t.index)
            .Select(t => t.container)
            .ToList();

        if (reverse)
        {
            ordered.Reverse();
        }

        return ordered;
    }

    /// <summary>
    /// Rejects requested service names the project does not know about. When <paramref name="file"/> is
    /// provided the universe of names is its declared services; otherwise it is the set of services that
    /// currently have a container (the only definition available for a project addressed only by name).
    /// </summary>
    private static void ValidateRequestedServices(
        ComposeFile? file,
        IReadOnlyList<ContainerInfo> containers,
        IReadOnlyList<string>? services)
    {
        if (services is not { Count: > 0 })
        {
            return;
        }

        var known = file is not null
            ? new HashSet<string>(file.Services.Keys, StringComparer.Ordinal)
            : new HashSet<string>(
                containers.Where(c => c.Service is not null).Select(c => c.Service!),
                StringComparer.Ordinal);

        ThrowIfUnknownServices(services.Where(s => !known.Contains(s)).ToList());
    }

    public async IAsyncEnumerable<ServiceLogLine> GetLogsAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        bool follow,
        int? tail,
        bool timestamps,
        string? since,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(providerName);
        var containers = await provider.ListContainersAsync(projectName, all: true, cancellationToken).ConfigureAwait(false);

        ValidateRequestedServices(file, containers, services);

        if (services is { Count: > 0 })
        {
            var requested = new HashSet<string>(services, StringComparer.Ordinal);
            containers = containers.Where(c => c.Service is not null && requested.Contains(c.Service)).ToList();
        }

        containers = OrderContainers(file, containers, reverse: false);

        if (containers.Count == 0)
        {
            yield break;
        }

        // We need each line's timestamp to display it (--timestamps) and to order a bounded dump; a live
        // (--follow) stream cannot be globally ordered, so timestamps are fetched there only to display.
        var withTimestamps = timestamps || !follow;

        if (!follow)
        {
            foreach (var line in await MergeByTimestampAsync(provider, containers, tail, withTimestamps, since, cancellationToken).ConfigureAwait(false))
            {
                yield return line;
            }

            yield break;
        }

        // Fan in: one pump task per container writes tagged lines into a shared channel so the caller
        // sees an interleaved stream, mirroring how `docker compose logs` merges multiple containers.
        var channel = Channel.CreateUnbounded<ServiceLogLine>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var pumpTasks = containers
            .Select(container => PumpLogsAsync(provider, container, channel.Writer, follow, tail, withTimestamps, since, cancellationToken))
            .ToArray();

        _ = Task.WhenAll(pumpTasks).ContinueWith(
            _ => channel.Writer.TryComplete(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        await foreach (var line in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return line;
        }
    }

    /// <summary>
    /// Collects the full (bounded) log output of every container in order, then merges it by timestamp
    /// so the combined dump reads chronologically instead of container-by-container. Lines without a
    /// parseable timestamp keep their collected position (stable order) and sort after timed lines.
    /// </summary>
    private static async Task<IReadOnlyList<ServiceLogLine>> MergeByTimestampAsync(
        IContainerProvider provider,
        IReadOnlyList<ContainerInfo> containers,
        int? tail,
        bool timestamps,
        string? since,
        CancellationToken cancellationToken)
    {
        var collected = new List<ServiceLogLine>();
        foreach (var container in containers)
        {
            var serviceName = container.Service ?? container.Name;
            await foreach (var line in provider.GetLogsAsync(container.Name, follow: false, tail, timestamps, since, cancellationToken).ConfigureAwait(false))
            {
                collected.Add(new ServiceLogLine(serviceName, line.Message, line.Timestamp));
            }
        }

        // OrderBy is stable, so equal (or absent) timestamps keep their collected order.
        return collected.OrderBy(l => l.Timestamp ?? DateTimeOffset.MaxValue).ToList();
    }

    private static async Task PumpLogsAsync(
        IContainerProvider provider,
        ContainerInfo container,
        ChannelWriter<ServiceLogLine> writer,
        bool follow,
        int? tail,
        bool timestamps,
        string? since,
        CancellationToken cancellationToken)
    {
        var serviceName = container.Service ?? container.Name;

        try
        {
            await foreach (var line in provider.GetLogsAsync(container.Name, follow, tail, timestamps, since, cancellationToken).ConfigureAwait(false))
            {
                await writer.WriteAsync(new ServiceLogLine(serviceName, line.Message, line.Timestamp), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the caller stops following.
        }
    }

    /// <summary>
    /// Shared driver for start/stop/restart: lists the project's existing containers, rejects unknown
    /// requested service names, optionally filters to the requested ones, orders them by the compose
    /// <c>depends_on</c> graph (reversed for teardown-style operations), then applies
    /// <paramref name="action"/> to each, capturing a per-service outcome instead of throwing on the
    /// first failure.
    /// </summary>
    private async Task<IReadOnlyList<ServiceOperationResult>> ApplyToContainersAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        string successStatus,
        bool reverseOrder,
        Func<IContainerProvider, string, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providerName);
        var containers = await provider.ListContainersAsync(projectName, all: true, cancellationToken).ConfigureAwait(false);

        ValidateRequestedServices(file, containers, services);

        if (services is { Count: > 0 })
        {
            var requested = new HashSet<string>(services, StringComparer.Ordinal);
            containers = containers.Where(c => c.Service is not null && requested.Contains(c.Service)).ToList();
        }

        containers = OrderContainers(file, containers, reverseOrder);

        var results = new List<ServiceOperationResult>();
        foreach (var container in containers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var serviceName = container.Service ?? container.Name;

            try
            {
                await action(provider, container.Name, cancellationToken).ConfigureAwait(false);
                results.Add(new ServiceOperationResult(serviceName, successStatus, container.Id));
            }
            catch (ProviderException ex)
            {
                results.Add(new ServiceOperationResult(serviceName, "failed", container.Id, ex.Message));
            }
        }

        return results;
    }

    private static async Task TryRemoveExistingAsync(IContainerProvider provider, string name, CancellationToken ct)
    {
        try
        {
            await provider.RemoveContainerAsync(name, force: true, ct).ConfigureAwait(false);
        }
        catch (ProviderException)
        {
            // No existing container to remove; ignore.
        }
    }

    private static ContainerRunSpec ToRunSpec(string projectName, ServiceSpec service, string image)
    {
        var spec = new ContainerRunSpec
        {
            Image = image,
            Name = WslccLabels.ContainerName(projectName, service.Name),
            Restart = service.Restart,
            HealthCheck = BuildContainerHealthCheck(service.HealthCheck),
            Detach = true,
        };

        spec.Labels[WslccLabels.Project] = projectName;
        spec.Labels[WslccLabels.Service] = service.Name;

        foreach (var env in service.Environment)
        {
            spec.Environment[env.Key] = env.Value;
        }

        foreach (var port in service.Ports)
        {
            spec.Ports.Add(port);
        }

        foreach (var token in service.Command)
        {
            spec.Command.Add(token);
        }

        return spec;
    }

    /// <summary>
    /// Translates a service's <c>healthcheck:</c> into the provider-agnostic <see cref="ContainerHealthCheck"/>.
    /// The Compose <c>test</c> array (<c>CMD-SHELL</c>/<c>CMD</c>/string short form) is flattened into a
    /// single shell command; <c>disable</c>/<c>NONE</c> becomes a disabled healthcheck.
    /// </summary>
    private static ContainerHealthCheck? BuildContainerHealthCheck(HealthCheckSpec? spec)
    {
        if (spec is null)
        {
            return null;
        }

        if (spec.Disabled)
        {
            return new ContainerHealthCheck { Disabled = true };
        }

        var command = ResolveHealthCommand(spec.Test);
        if (command is null
            && spec.Interval is null && spec.Timeout is null && spec.Retries is null && spec.StartPeriod is null)
        {
            return null; // nothing to apply beyond whatever the image already declares
        }

        return new ContainerHealthCheck
        {
            Command = command,
            Interval = spec.Interval,
            Timeout = spec.Timeout,
            Retries = spec.Retries,
            StartPeriod = spec.StartPeriod,
        };
    }

    private static string? ResolveHealthCommand(IList<string> test)
    {
        if (test.Count == 0)
        {
            return null;
        }

        // Compose forms: ["CMD-SHELL", "<shell cmd>"], ["CMD", "<argv>", ...], or a string short form
        // (stored as a single element). The container CLI's --health-cmd runs via a shell either way.
        if (string.Equals(test[0], "CMD-SHELL", StringComparison.Ordinal)
            || string.Equals(test[0], "CMD", StringComparison.Ordinal))
        {
            return test.Count > 1 ? string.Join(" ", test.Skip(1)) : null;
        }

        return string.Join(" ", test);
    }

    /// <summary>Depth-first topological ordering by <c>depends_on</c>, tolerant of cycles/missing deps.</summary>
    internal static IReadOnlyList<ServiceSpec> OrderServices(ComposeFile file)
    {
        var ordered = new List<ServiceSpec>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var inProgress = new HashSet<string>(StringComparer.Ordinal);

        void Visit(string name)
        {
            if (visited.Contains(name))
            {
                return;
            }

            if (!file.Services.TryGetValue(name, out var service))
            {
                return; // dependency on an unknown service; ignore
            }

            if (!inProgress.Add(name))
            {
                return; // cycle guard
            }

            foreach (var dependency in service.DependsOn)
            {
                Visit(dependency.Name);
            }

            inProgress.Remove(name);
            visited.Add(name);
            ordered.Add(service);
        }

        foreach (var name in file.Services.Keys)
        {
            Visit(name);
        }

        return ordered;
    }

    private IContainerProvider ResolveProvider(string? providerName)
    {
        if (_providers.Count == 0)
        {
            throw new InvalidOperationException("No container providers are registered.");
        }

        var requested = string.IsNullOrWhiteSpace(providerName) ? _defaultProvider : providerName;

        if (string.IsNullOrWhiteSpace(requested))
        {
            return _providers[0];
        }

        var match = _providers.FirstOrDefault(
            p => string.Equals(p.Name, requested, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            throw new InvalidOperationException(
                $"Unknown provider '{requested}'. Available providers: {string.Join(", ", ProviderNames)}.");
        }

        return match;
    }
}

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

    /// <summary>Creates an engine driving the given providers.</summary>
    /// <param name="providers">The providers the engine can drive; at least one is needed to run anything.</param>
    /// <param name="defaultProvider">
    /// Name of the provider used when a call does not name one. When blank, the first registered
    /// provider is used.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="providers"/> is <c>null</c>.</exception>
    public ComposeEngine(IEnumerable<IContainerProvider> providers, string? defaultProvider = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers.ToList();
        _defaultProvider = string.IsNullOrWhiteSpace(defaultProvider) ? null : defaultProvider;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ProviderNames => _providers.Select(p => p.Name).ToList();

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProviderInfo>> GetProviderInfosAsync(CancellationToken cancellationToken = default)
    {
        var tasks = _providers.Select(p => p.GetProviderInfoAsync(cancellationToken));
        var infos = await Task.WhenAll(tasks).ConfigureAwait(false);
        return infos;
    }

    /// <inheritdoc/>
    public Task<ProviderInfo> GetProviderInfoAsync(string? providerName, CancellationToken cancellationToken = default)
        => ResolveProvider(providerName).GetProviderInfoAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ServiceOperationResult>> UpAsync(
        string projectName,
        ComposeFile file,
        string? providerName,
        bool pull,
        BuildPolicy buildPolicy,
        string? baseDirectory,
        IReadOnlyDictionary<string, string>? serviceConfigHashes = null,
        IProgress<ServiceProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RequireProjectName(projectName);
        ArgumentNullException.ThrowIfNull(file);

        var provider = ResolveProvider(providerName);

        // Compose file — container_name: two services may not resolve to the same container name.
        EnsureUniqueContainerNames(projectName, file);

        var existingByService = await ListByServiceAsync(provider, projectName, cancellationToken).ConfigureAwait(false);

        // Create the project's networks and named volumes up front so every service can attach to them.
        await ProvisionResourcesAsync(provider, projectName, file, cancellationToken).ConfigureAwait(false);

        var run = new UpRun
        {
            Provider = provider,
            ProjectName = projectName,
            File = file,
            BaseDirectory = baseDirectory,
            Pull = pull,
            BuildPolicy = buildPolicy,

            // --pull / --build ask for fresh images, so containers are always recreated to pick them up;
            // otherwise recreation is driven by the per-service config hash.
            ForceRecreate = pull || buildPolicy == BuildPolicy.Always,
            Existing = existingByService,
            ConfigHashes = serviceConfigHashes,
            Progress = progress,
        };

        // Compose file — depends_on: services start in dependency order.
        foreach (var service in OrderServices(file))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await UpServiceAsync(run, service, cancellationToken).ConfigureAwait(false);
        }

        return run.Results;
    }

    /// <summary>Everything the per-service steps of a single <see cref="UpAsync"/> call share.</summary>
    private sealed class UpRun
    {
        public required IContainerProvider Provider { get; init; }

        public required string ProjectName { get; init; }

        public required ComposeFile File { get; init; }

        public string? BaseDirectory { get; init; }

        public bool Pull { get; init; }

        public BuildPolicy BuildPolicy { get; init; }

        /// <summary>When set, an existing container is replaced even if its config hash still matches.</summary>
        public bool ForceRecreate { get; init; }

        /// <summary>The project's containers as they were before this run, indexed by service name.</summary>
        public required IReadOnlyDictionary<string, ContainerInfo> Existing { get; init; }

        public IReadOnlyDictionary<string, string>? ConfigHashes { get; init; }

        public IProgress<ServiceProgressUpdate>? Progress { get; init; }

        /// <summary>One entry per service processed so far, in start order.</summary>
        public List<ServiceOperationResult> Results { get; } = new();

        /// <summary>Container name of every service that is up, keyed by service name.</summary>
        public Dictionary<string, string> StartedContainers { get; } = new(StringComparer.Ordinal);

        /// <summary>Services that failed, so their dependents can be aborted too.</summary>
        public HashSet<string> Failed { get; } = new(StringComparer.Ordinal);

        public string? ConfigHashOf(string serviceName) => ConfigHashes?.GetValueOrDefault(serviceName);
    }

    /// <summary>
    /// Brings a single service up: aborts it when a required dependency failed, reuses an unchanged
    /// running container when it can, and otherwise creates a fresh one. A provider failure is captured
    /// as this service's outcome rather than aborting the whole run.
    /// </summary>
    private static async Task UpServiceAsync(UpRun run, ServiceSpec service, CancellationToken cancellationToken)
    {
        var handled = TryAbortForFailedDependency(run, service) || TryReuseExistingContainer(run, service);
        if (handled)
            return;

        try
        {
            await CreateServiceContainerAsync(run, service, cancellationToken).ConfigureAwait(false);
        }
        catch (ProviderException ex)
        {
            run.Failed.Add(service.Name);
            run.Results.Add(new ServiceOperationResult(service.Name, "failed", Error: ex.Message));
            Report(run.Progress, service.Name, "creating", "failed", ex.Message);
        }
    }

    /// <summary>
    /// Compose file — depends_on: a service whose <c>required</c> dependency did not come up is not
    /// started (docker-compose aborts dependents), and it in turn fails its own dependents.
    /// </summary>
    private static bool TryAbortForFailedDependency(UpRun run, ServiceSpec service)
    {
        var broken = service.DependsOn.FirstOrDefault(d => d.Required && run.Failed.Contains(d.Name));
        if (broken is null)
            return false;

        var error = $"dependency '{broken.Name}' failed to start";
        run.Results.Add(new ServiceOperationResult(service.Name, "failed", Error: error));
        run.Failed.Add(service.Name);
        Report(run.Progress, service.Name, "creating", "failed", error);
        return true;
    }

    /// <summary>
    /// Leaves an unchanged, still-running container in place instead of recreating it. Requires a known
    /// config hash on both sides, so a project brought up without change detection always recreates.
    /// </summary>
    private static bool TryReuseExistingContainer(UpRun run, ServiceSpec service)
    {
        var existing = run.Existing.GetValueOrDefault(service.Name);
        if (existing is null)
            return false;

        if (!CanReuse(run, existing, run.ConfigHashOf(service.Name)))
            return false;

        run.StartedContainers[service.Name] = existing.Name;
        run.Results.Add(new ServiceOperationResult(service.Name, "running", existing.Id));
        Report(run.Progress, service.Name, "creating", "running", containerId: existing.Id);
        return true;
    }

    /// <summary>Whether an existing container is still running under the same configuration hash.</summary>
    private static bool CanReuse(UpRun run, ContainerInfo existing, string? configHash)
    {
        if (run.ForceRecreate)
            return false;

        if (!IsRunning(existing))
            return false;

        if (configHash is not { Length: > 0 })
            return false;

        return string.Equals(existing.ConfigHash, configHash, StringComparison.Ordinal);
    }

    /// <summary>
    /// Waits for the service's dependencies, makes its image available, then replaces any container of
    /// the same name with a freshly created one and attaches it to the rest of its networks.
    /// </summary>
    private static async Task CreateServiceContainerAsync(UpRun run, ServiceSpec service, CancellationToken cancellationToken)
    {
        if (service.DependsOn.Count > 0)
        {
            Report(run.Progress, service.Name, "waiting", ServiceProgressUpdate.InProgress);
            await WaitForDependenciesAsync(
                    run.Provider, run.ProjectName, run.File, service, run.StartedContainers, cancellationToken)
                .ConfigureAwait(false);
        }

        var image = await PrepareServiceImageAsync(
                run.Provider, run.ProjectName, service, run.BaseDirectory, run.Pull, run.BuildPolicy, run.Progress, cancellationToken)
            .ConfigureAwait(false);

        Report(run.Progress, service.Name, "creating", ServiceProgressUpdate.InProgress);

        var (spec, networks) = BuildServiceRunSpec(run, service, image);

        await TryRemoveExistingAsync(run.Provider, spec.Name, cancellationToken).ConfigureAwait(false);
        var id = await run.Provider.RunContainerAsync(spec, cancellationToken).ConfigureAwait(false);

        // The container was created on its first network; the rest are connected now that it is running.
        for (var i = 1; i < networks.Count; i++)
            await run.Provider.ConnectNetworkAsync(networks[i], spec.Name, service.Name, cancellationToken).ConfigureAwait(false);

        run.StartedContainers[service.Name] = spec.Name;
        run.Results.Add(new ServiceOperationResult(service.Name, "started", id));
        Report(run.Progress, service.Name, "creating", "started", containerId: id);
    }

    /// <summary>
    /// Turns a service into the run spec for this project, adding the change-detection label, the
    /// network it is created on and its resolved mounts. Also returns the service's full network list,
    /// whose tail the caller connects after the container is running.
    /// </summary>
    private static (ContainerRunSpec Spec, IReadOnlyList<string> Networks) BuildServiceRunSpec(
        UpRun run,
        ServiceSpec service,
        string image)
    {
        var spec = ToRunSpec(run.ProjectName, service, image, run.BaseDirectory);

        if (run.ConfigHashOf(service.Name) is { Length: > 0 } configHash)
            spec.Labels[WslccLabels.ConfigHash] = configHash;

        var networks = ResolveServiceNetworks(run.File, run.ProjectName, service);
        if (networks.Count > 0)
        {
            // Compose file — networks: the service name is registered as an alias on its first network.
            spec.Network = networks[0];
            spec.NetworkAlias = service.Name;
        }

        // Compose file — volumes / tmpfs: resolve named volumes and bind sources; create_host_path mkdir.
        foreach (var mount in service.Volumes)
            spec.Volumes.Add(ResolveMount(run.File, run.ProjectName, run.BaseDirectory, mount));

        return (spec, networks);
    }

    private static void Report(
        IProgress<ServiceProgressUpdate>? progress,
        string service,
        string phase,
        string status,
        string? message = null,
        string? containerId = null)
        => progress?.Report(new ServiceProgressUpdate(service, phase, status, message, containerId));

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
                byService[service] = container;
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
            if (dependency.Condition == DependencyCondition.ServiceStarted)
                continue;

            if (!file.Services.TryGetValue(dependency.Name, out var dependencyService))
                continue;

            var container = startedContainers.TryGetValue(dependency.Name, out var name)
                ? name
                : ResolveContainerName(projectName, dependencyService);

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
                return;

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
                    return false;

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
    /// Resolves the image to run for a service and makes sure it is available: a <c>build:</c> service is
    /// built, an image-only service is pulled. Returns the image the container should run; throws
    /// <see cref="ProviderException"/> when nothing runnable is defined.
    /// </summary>
    private static Task<string> PrepareServiceImageAsync(
        IContainerProvider provider,
        string projectName,
        ServiceSpec service,
        string? baseDirectory,
        bool pull,
        BuildPolicy buildPolicy,
        IProgress<ServiceProgressUpdate>? progress,
        CancellationToken cancellationToken)
        => service.Build is not null
            ? EnsureBuiltImageAsync(provider, projectName, service, baseDirectory, buildPolicy, progress, cancellationToken)
            : EnsurePulledImageAsync(provider, service, pull, progress, cancellationToken);

    /// <summary>
    /// Compose file — build: the service's image is built and tagged as its <c>image:</c> (or
    /// <c>&lt;project&gt;-&lt;service&gt;</c>). <see cref="BuildPolicy.Always"/> rebuilds every time,
    /// <see cref="BuildPolicy.Never"/> fails when the image is missing, and <see cref="BuildPolicy.Auto"/>
    /// builds only when it is missing (matching docker-compose's default <c>up</c>).
    /// </summary>
    private static async Task<string> EnsureBuiltImageAsync(
        IContainerProvider provider,
        string projectName,
        ServiceSpec service,
        string? baseDirectory,
        BuildPolicy buildPolicy,
        IProgress<ServiceProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var (spec, error) = CreateBuildSpec(projectName, service, baseDirectory);
        if (spec is null)
            throw new ProviderException(error!);

        switch (buildPolicy)
        {
            case BuildPolicy.Always:
                await BuildImageAsync(provider, spec, service.Name, progress, cancellationToken).ConfigureAwait(false);
                break;

            case BuildPolicy.Never:
                var imageExists = await provider.ImageExistsAsync(spec.Tag, cancellationToken).ConfigureAwait(false);
                if (!imageExists)
                {
                    throw new ProviderException(
                        $"image '{spec.Tag}' is not present and --no-build was set; run 'wslcc compose build' first");
                }

                break;

            default:
                var imageMissing = !await provider.ImageExistsAsync(spec.Tag, cancellationToken).ConfigureAwait(false);
                if (imageMissing)
                    await BuildImageAsync(provider, spec, service.Name, progress, cancellationToken).ConfigureAwait(false);

                break;
        }

        return spec.Tag;
    }

    private static async Task BuildImageAsync(
        IContainerProvider provider,
        ImageBuildSpec spec,
        string serviceName,
        IProgress<ServiceProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        Report(progress, serviceName, "building", ServiceProgressUpdate.InProgress);
        await provider.BuildImageAsync(spec, cancellationToken).ConfigureAwait(false);
        Report(progress, serviceName, "building", "built");
    }

    /// <summary>
    /// Compose file — image: a service that only references an image has it pulled when missing, or
    /// always when <paramref name="pull"/> is set. Only the missing/forced case reports progress, so an
    /// already-present image stays silent.
    /// </summary>
    private static async Task<string> EnsurePulledImageAsync(
        IContainerProvider provider,
        ServiceSpec service,
        bool pull,
        IProgress<ServiceProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(service.Image))
            throw new ProviderException("no 'image' or 'build:' section specified");

        var needsPull = pull || !await provider.ImageExistsAsync(service.Image!, cancellationToken).ConfigureAwait(false);
        if (needsPull)
        {
            Report(progress, service.Name, "pulling", ServiceProgressUpdate.InProgress);
            await provider.EnsureImageAsync(service.Image!, pull, cancellationToken).ConfigureAwait(false);
            Report(progress, service.Name, "pulling", "pulled");
        }
        else
        {
            await provider.EnsureImageAsync(service.Image!, pull, cancellationToken).ConfigureAwait(false);
        }

        return service.Image!;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ServiceOperationResult>> DownAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        bool removeVolumes = false,
        IProgress<ServiceProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RequireProjectName(projectName);

        var provider = ResolveProvider(providerName);
        var containers = await provider.ListContainersAsync(projectName, all: true, cancellationToken).ConfigureAwait(false);

        // Compose file — depends_on: tear down in reverse dependency order (dependents first), like `stop`.
        containers = OrderContainers(file, containers, reverse: true);

        var results = new List<ServiceOperationResult>();
        foreach (var container in containers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StopAndRemoveContainerAsync(provider, container, results, progress, cancellationToken).ConfigureAwait(false);
        }

        // Remove the networks wslcc created for the project (once their containers are gone). Named
        // volumes are kept unless explicitly requested, matching `docker compose down` (data is precious).
        await RemoveProjectResourcesAsync(provider, projectName, removeVolumes, results, progress, cancellationToken)
            .ConfigureAwait(false);

        return results;
    }

    /// <summary>
    /// Stops a container and removes it, recording the outcome. The stop is best-effort (an already
    /// stopped or unstoppable container is still removed); only a failed removal is reported as a failure.
    /// </summary>
    private static async Task StopAndRemoveContainerAsync(
        IContainerProvider provider,
        ContainerInfo container,
        List<ServiceOperationResult> results,
        IProgress<ServiceProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var serviceName = container.Service ?? container.Name;
        Report(progress, serviceName, "removing", ServiceProgressUpdate.InProgress, containerId: container.Id);

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
            Report(progress, serviceName, "removing", "removed", containerId: container.Id);
        }
        catch (ProviderException ex)
        {
            results.Add(new ServiceOperationResult(serviceName, "failed", container.Id, ex.Message));
            Report(progress, serviceName, "removing", "failed", ex.Message, container.Id);
        }
    }

    /// <summary>
    /// Best-effort teardown of the project's networks (always) and named volumes (only when requested).
    /// Both are discovered by their <c>wslcc.project</c> label, so external resources — which wslcc never
    /// labelled — are left untouched.
    /// </summary>
    private static async Task RemoveProjectResourcesAsync(
        IContainerProvider provider,
        string projectName,
        bool removeVolumes,
        List<ServiceOperationResult> results,
        IProgress<ServiceProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        await RemoveLabelledResourcesAsync(
                "network",
                () => provider.ListNetworkNamesAsync(projectName, cancellationToken),
                name => provider.RemoveNetworkAsync(name, cancellationToken),
                results,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        if (!removeVolumes)
            return;

        await RemoveLabelledResourcesAsync(
                "volume",
                () => provider.ListVolumeNamesAsync(projectName, cancellationToken),
                name => provider.RemoveVolumeAsync(name, cancellationToken),
                results,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Removes every resource <paramref name="list"/> reports, recording a per-resource outcome under the
    /// label "<paramref name="kind"/> &lt;name&gt;". A listing failure is swallowed (nothing to remove) so
    /// container removal still reports.
    /// </summary>
    private static async Task RemoveLabelledResourcesAsync(
        string kind,
        Func<Task<IReadOnlyList<string>>> list,
        Func<string, Task> remove,
        List<ServiceOperationResult> results,
        IProgress<ServiceProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> names;
        try
        {
            names = await list().ConfigureAwait(false);
        }
        catch (ProviderException)
        {
            return;
        }

        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var label = $"{kind} {name}";
            Report(progress, label, "removing", ServiceProgressUpdate.InProgress);
            try
            {
                await remove(name).ConfigureAwait(false);
                results.Add(new ServiceOperationResult(label, "removed"));
                Report(progress, label, "removing", "removed");
            }
            catch (ProviderException ex)
            {
                results.Add(new ServiceOperationResult(label, "failed", Error: ex.Message));
                Report(progress, label, "removing", "failed", ex.Message);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ContainerInfo>> PsAsync(
        string? projectName,
        string? providerName,
        bool all,
        IProgress<ServiceProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Report(progress, string.Empty, "listing", ServiceProgressUpdate.InProgress);
        var containers = await ResolveProvider(providerName)
            .ListContainersAsync(projectName, all, cancellationToken)
            .ConfigureAwait(false);
        Report(progress, string.Empty, "listing", "listed", message: $"{containers.Count} container(s)");
        return containers;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ServiceOperationResult>> StartAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        IProgress<ServiceProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RequireProjectName(projectName);

        return ApplyToContainersAsync(
            projectName, file, providerName, services, "starting", "started", reverseOrder: false,
            (provider, containerName, ct) => provider.StartContainerAsync(containerName, ct),
            progress, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ServiceOperationResult>> StopAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        IProgress<ServiceProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RequireProjectName(projectName);

        return ApplyToContainersAsync(
            projectName, file, providerName, services, "stopping", "stopped", reverseOrder: true,
            (provider, containerName, ct) => provider.StopContainerAsync(containerName, ct),
            progress, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ServiceOperationResult>> RestartAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        IProgress<ServiceProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RequireProjectName(projectName);

        return ApplyToContainersAsync(
            projectName, file, providerName, services, "restarting", "restarted", reverseOrder: false,
            (provider, containerName, ct) => provider.RestartContainerAsync(containerName, ct),
            progress, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ServiceOperationResult>> PullAsync(
        ComposeFile file,
        string? providerName,
        IReadOnlyList<string>? services,
        IProgress<ServiceProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var provider = ResolveProvider(providerName);
        var results = new List<ServiceOperationResult>();

        foreach (var service in SelectServices(file, services))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Compose file — image: build-only services have nothing to pull (as in `docker compose pull`).
            if (string.IsNullOrWhiteSpace(service.Image))
                continue;

            await PullServiceAsync(provider, service, results, progress, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    private static async Task PullServiceAsync(
        IContainerProvider provider,
        ServiceSpec service,
        List<ServiceOperationResult> results,
        IProgress<ServiceProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        Report(progress, service.Name, "pulling", ServiceProgressUpdate.InProgress);
        try
        {
            await provider.EnsureImageAsync(service.Image!, alwaysPull: true, cancellationToken).ConfigureAwait(false);
            results.Add(new ServiceOperationResult(service.Name, "pulled"));
            Report(progress, service.Name, "pulling", "pulled");
        }
        catch (ProviderException ex)
        {
            results.Add(new ServiceOperationResult(service.Name, "failed", Error: ex.Message));
            Report(progress, service.Name, "pulling", "failed", ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ServiceOperationResult>> BuildAsync(
        string projectName,
        ComposeFile file,
        string? providerName,
        string? baseDirectory,
        IReadOnlyList<string>? services,
        IProgress<ServiceProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RequireProjectName(projectName);
        ArgumentNullException.ThrowIfNull(file);

        var provider = ResolveProvider(providerName);
        var results = new List<ServiceOperationResult>();

        foreach (var service in SelectServices(file, services))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Compose file — build: services that only reference a pre-built image are skipped silently.
            if (service.Build is null)
                continue;

            await BuildServiceAsync(provider, projectName, service, baseDirectory, results, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        return results;
    }

    /// <summary>
    /// Builds one service's image, recording the outcome. An unusable <c>build:</c> section and a
    /// provider failure are both captured as this service's failure rather than thrown.
    /// </summary>
    private static async Task BuildServiceAsync(
        IContainerProvider provider,
        string projectName,
        ServiceSpec service,
        string? baseDirectory,
        List<ServiceOperationResult> results,
        IProgress<ServiceProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var (spec, error) = CreateBuildSpec(projectName, service, baseDirectory);
        if (spec is null)
        {
            results.Add(new ServiceOperationResult(service.Name, "failed", Error: error));
            Report(progress, service.Name, "building", "failed", error);
            return;
        }

        try
        {
            await BuildImageAsync(provider, spec, service.Name, progress, cancellationToken).ConfigureAwait(false);
            results.Add(new ServiceOperationResult(service.Name, "built"));
        }
        catch (ProviderException ex)
        {
            results.Add(new ServiceOperationResult(service.Name, "failed", Error: ex.Message));
            Report(progress, service.Name, "building", "failed", ex.Message);
        }
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
            return (null, "'build' has no context");

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
            spec.Args[arg.Key] = arg.Value;

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
            return null;

        if (Path.IsPathRooted(context) || string.IsNullOrWhiteSpace(baseDirectory))
            return context;

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
        await EnsureProjectVolumesAsync(provider, projectName, file, cancellationToken).ConfigureAwait(false);
        await EnsureProjectNetworksAsync(provider, projectName, file, cancellationToken).ConfigureAwait(false);
        await EnsureDefaultNetworkAsync(provider, projectName, file, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Compose file — volumes (top level): declared volumes are created project-prefixed; <c>external</c> ones are left alone.</summary>
    private static async Task EnsureProjectVolumesAsync(
        IContainerProvider provider,
        string projectName,
        ComposeFile file,
        CancellationToken cancellationToken)
    {
        foreach (var kvp in file.Volumes)
        {
            if (kvp.Value.External)
                continue;

            var spec = new VolumeCreateSpec { Name = WslccLabels.VolumeName(projectName, kvp.Key), Driver = kvp.Value.Driver };
            spec.Labels[WslccLabels.Project] = projectName;
            spec.Labels[WslccLabels.Volume] = kvp.Key;
            await provider.EnsureVolumeAsync(spec, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Compose file — networks (top level): declared networks are created project-prefixed; <c>external</c> ones are left alone.</summary>
    private static async Task EnsureProjectNetworksAsync(
        IContainerProvider provider,
        string projectName,
        ComposeFile file,
        CancellationToken cancellationToken)
    {
        foreach (var kvp in file.Networks)
        {
            if (kvp.Value.External)
                continue;

            var spec = new NetworkCreateSpec { Name = WslccLabels.NetworkName(projectName, kvp.Key), Driver = kvp.Value.Driver };
            spec.Labels[WslccLabels.Project] = projectName;
            spec.Labels[WslccLabels.Network] = kvp.Key;
            await provider.EnsureNetworkAsync(spec, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Compose file — networks (service): a service that lists none joins the implicit <c>&lt;project&gt;_default</c> network.</summary>
    private static async Task EnsureDefaultNetworkAsync(
        IContainerProvider provider,
        string projectName,
        ComposeFile file,
        CancellationToken cancellationToken)
    {
        var anyServiceWithoutNetworks = file.Services.Values.Any(s => s.Networks.Count == 0);
        if (!anyServiceWithoutNetworks)
            return;

        var spec = new NetworkCreateSpec { Name = WslccLabels.DefaultNetworkName(projectName) };
        spec.Labels[WslccLabels.Project] = projectName;
        spec.Labels[WslccLabels.Network] = "default";
        await provider.EnsureNetworkAsync(spec, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The networks a service attaches to: the ones it lists (mapped to their project-prefixed names, or
    /// their bare name when the network is <c>external</c>), or the implicit default network when it
    /// lists none.
    /// </summary>
    private static IReadOnlyList<string> ResolveServiceNetworks(ComposeFile file, string projectName, ServiceSpec service)
    {
        if (service.Networks.Count == 0)
            return new[] { WslccLabels.DefaultNetworkName(projectName) };

        return service.Networks
            .Select(key => file.Networks.TryGetValue(key, out var spec) && spec.External
                ? key
                : WslccLabels.NetworkName(projectName, key))
            .ToList();
    }

    /// <summary>
    /// Resolves a service mount: named-volume sources are mapped to their project-prefixed name (unless
    /// external or undeclared), relative bind sources are resolved against the project directory, and
    /// anonymous volumes / tmpfs pass through with an unresolved source. When
    /// <see cref="ServiceMount.BindCreateHostPath"/> is true, missing bind host directories are created.
    /// </summary>
    private static ServiceMount ResolveMount(
        ComposeFile file,
        string projectName,
        string? baseDirectory,
        ServiceMount mount)
    {
        if (mount.Type is MountType.Tmpfs)
        {
            return CloneMount(mount);
        }

        if (mount.Type is MountType.Bind)
        {
            var source = mount.Source ?? string.Empty;
            var resolved = ResolveBindSource(source, baseDirectory);
            if (mount.BindCreateHostPath == true)
                EnsureBindHostPath(resolved);

            var resolvedBind = CloneMount(mount);
            resolvedBind.Source = resolved;
            return resolvedBind;
        }

        // Volume: anonymous (no source) or named.
        if (string.IsNullOrEmpty(mount.Source))
            return CloneMount(mount);

        var resolvedVolume = CloneMount(mount);
        resolvedVolume.Source = ResolveVolumeSource(file, projectName, mount.Source);
        return resolvedVolume;
    }

    private static ServiceMount CloneMount(ServiceMount mount)
        => new()
        {
            Type = mount.Type,
            Source = mount.Source,
            Target = mount.Target,
            ReadOnly = mount.ReadOnly,
            VolumeNocopy = mount.VolumeNocopy,
            VolumeSubpath = mount.VolumeSubpath,
            BindPropagation = mount.BindPropagation,
            BindCreateHostPath = mount.BindCreateHostPath,
            BindSelinux = mount.BindSelinux,
            BindRecursive = mount.BindRecursive,
            TmpfsSize = mount.TmpfsSize,
            TmpfsMode = mount.TmpfsMode,
            TmpfsExtraOptions = mount.TmpfsExtraOptions,
        };

    private static void EnsureBindHostPath(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || source.StartsWith('~'))
            return;

        try
        {
            if (!Directory.Exists(source) && !File.Exists(source))
                Directory.CreateDirectory(source);
        }
        catch (IOException)
        {
            // Runtime will surface the failure if the path remains unusable.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string ResolveBindSource(string source, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            return source;

        var alreadyAbsolute = Path.IsPathRooted(source) || source.StartsWith('~');
        if (alreadyAbsolute)
            return source;

        return Path.GetFullPath(Path.Combine(baseDirectory, source));
    }

    private static string ResolveVolumeSource(ComposeFile file, string projectName, string source)
    {
        if (file.Volumes.TryGetValue(source, out var spec))
            return spec.External ? source : WslccLabels.VolumeName(projectName, source);

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
            return file.Services.Values;

        var selected = new List<ServiceSpec>();
        var unknown = new List<string>();
        foreach (var name in services)
        {
            if (file.Services.TryGetValue(name, out var service))
                selected.Add(service);
            else
                unknown.Add(name);
        }

        ThrowIfUnknownServices(unknown);
        return selected;
    }

    /// <summary>Throws a <see cref="ProviderException"/> listing any service names that were not found.</summary>
    private static void ThrowIfUnknownServices(IReadOnlyList<string> unknown)
    {
        if (unknown.Count > 0)
            throw new ProviderException($"no such service: {string.Join(", ", unknown)}");
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
            return containers;

        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        var next = 0;
        foreach (var service in OrderServices(file))
            rank[service.Name] = next++;

        int RankOf(ContainerInfo c)
            => c.Service is not null && rank.TryGetValue(c.Service, out var r) ? r : int.MaxValue;

        var ordered = containers
            .Select((container, index) => (container, index))
            .OrderBy(t => RankOf(t.container))
            .ThenBy(t => t.index)
            .Select(t => t.container)
            .ToList();

        if (reverse)
            ordered.Reverse();

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
            return;

        var known = file is not null
            ? new HashSet<string>(file.Services.Keys, StringComparer.Ordinal)
            : new HashSet<string>(
                containers.Where(c => c.Service is not null).Select(c => c.Service!),
                StringComparer.Ordinal);

        ThrowIfUnknownServices(services.Where(s => !known.Contains(s)).ToList());
    }

    /// <summary>
    /// Lists the project's existing containers, rejects unknown requested service names, narrows the list
    /// to the requested services when any were named, and orders it by the compose <c>depends_on</c>
    /// graph (reversed for teardown-style operations).
    /// </summary>
    private static async Task<IReadOnlyList<ContainerInfo>> SelectOrderedContainersAsync(
        IContainerProvider provider,
        string projectName,
        ComposeFile? file,
        IReadOnlyList<string>? services,
        bool reverseOrder,
        CancellationToken cancellationToken)
    {
        var containers = await provider.ListContainersAsync(projectName, all: true, cancellationToken).ConfigureAwait(false);

        ValidateRequestedServices(file, containers, services);

        if (services is { Count: > 0 })
        {
            var requested = new HashSet<string>(services, StringComparer.Ordinal);
            containers = containers.Where(c => c.Service is not null && requested.Contains(c.Service)).ToList();
        }

        return OrderContainers(file, containers, reverseOrder);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<ServiceLogLine> GetLogsAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        bool follow,
        int? tail,
        bool timestamps,
        string? since,
        CancellationToken cancellationToken = default)
    {
        RequireProjectName(projectName);
        if (tail is { } tailLines)
            ArgumentOutOfRangeException.ThrowIfNegative(tailLines, nameof(tail));

        return GetLogsCoreAsync(
            projectName, file, providerName, services, follow, tail, timestamps, since, cancellationToken);
    }

    private async IAsyncEnumerable<ServiceLogLine> GetLogsCoreAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        bool follow,
        int? tail,
        bool timestamps,
        string? since,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providerName);
        var containers = await SelectOrderedContainersAsync(
                provider, projectName, file, services, reverseOrder: false, cancellationToken)
            .ConfigureAwait(false);

        if (containers.Count == 0)
            yield break;

        // We need each line's timestamp to display it (--timestamps) and to order a bounded dump; a live
        // (--follow) stream cannot be globally ordered, so timestamps are fetched there only to display.
        var withTimestamps = timestamps || !follow;

        var lines = follow
            ? StreamMergedLogsAsync(provider, containers, tail, withTimestamps, since, cancellationToken)
            : ReadMergedLogsAsync(provider, containers, tail, withTimestamps, since, cancellationToken);

        await foreach (var line in lines.ConfigureAwait(false))
            yield return line;
    }

    /// <summary>
    /// Yields the bounded log output of every container, merged by timestamp so the combined dump reads
    /// chronologically instead of container-by-container. Lines without a parseable timestamp keep their
    /// collected position (stable order) and sort after timed lines.
    /// </summary>
    private static async IAsyncEnumerable<ServiceLogLine> ReadMergedLogsAsync(
        IContainerProvider provider,
        IReadOnlyList<ContainerInfo> containers,
        int? tail,
        bool timestamps,
        string? since,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var collected = new List<ServiceLogLine>();
        foreach (var container in containers)
        {
            var serviceName = container.Service ?? container.Name;
            await foreach (var line in provider.GetLogsAsync(container.Name, follow: false, tail, timestamps, since, cancellationToken).ConfigureAwait(false))
                collected.Add(new ServiceLogLine(serviceName, line.Message, line.Timestamp));
        }

        // OrderBy is stable, so equal (or absent) timestamps keep their collected order.
        foreach (var line in collected.OrderBy(l => l.Timestamp ?? DateTimeOffset.MaxValue))
            yield return line;
    }

    /// <summary>
    /// Fans in a live stream: one pump task per container writes tagged lines into a shared channel so the
    /// caller sees an interleaved stream, mirroring how <c>docker compose logs --follow</c> merges
    /// multiple containers. Completes once every pump has finished.
    /// </summary>
    private static async IAsyncEnumerable<ServiceLogLine> StreamMergedLogsAsync(
        IContainerProvider provider,
        IReadOnlyList<ContainerInfo> containers,
        int? tail,
        bool timestamps,
        string? since,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<ServiceLogLine>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var pumpTasks = containers
            .Select(container => PumpLogsAsync(provider, container, channel.Writer, follow: true, tail, timestamps, since, cancellationToken))
            .ToArray();

        _ = Task.WhenAll(pumpTasks).ContinueWith(
            _ => channel.Writer.TryComplete(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        await foreach (var line in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return line;
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
                await writer.WriteAsync(new ServiceLogLine(serviceName, line.Message, line.Timestamp), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when the caller stops following.
        }
    }

    /// <summary>
    /// Shared driver for start/stop/restart: selects and orders the project's containers, then applies
    /// <paramref name="action"/> to each, capturing a per-service outcome instead of throwing on the
    /// first failure.
    /// </summary>
    private async Task<IReadOnlyList<ServiceOperationResult>> ApplyToContainersAsync(
        string projectName,
        ComposeFile? file,
        string? providerName,
        IReadOnlyList<string>? services,
        string phase,
        string successStatus,
        bool reverseOrder,
        Func<IContainerProvider, string, CancellationToken, Task> action,
        IProgress<ServiceProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providerName);
        var containers = await SelectOrderedContainersAsync(
                provider, projectName, file, services, reverseOrder, cancellationToken)
            .ConfigureAwait(false);

        var results = new List<ServiceOperationResult>();
        foreach (var container in containers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ApplyToContainerAsync(
                    provider, container, phase, successStatus, action, results, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        return results;
    }

    /// <summary>Applies one start/stop/restart action to a container, recording success or failure.</summary>
    private static async Task ApplyToContainerAsync(
        IContainerProvider provider,
        ContainerInfo container,
        string phase,
        string successStatus,
        Func<IContainerProvider, string, CancellationToken, Task> action,
        List<ServiceOperationResult> results,
        IProgress<ServiceProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var serviceName = container.Service ?? container.Name;

        Report(progress, serviceName, phase, ServiceProgressUpdate.InProgress, containerId: container.Id);
        try
        {
            await action(provider, container.Name, cancellationToken).ConfigureAwait(false);
            results.Add(new ServiceOperationResult(serviceName, successStatus, container.Id));
            Report(progress, serviceName, phase, successStatus, containerId: container.Id);
        }
        catch (ProviderException ex)
        {
            results.Add(new ServiceOperationResult(serviceName, "failed", container.Id, ex.Message));
            Report(progress, serviceName, phase, "failed", ex.Message, container.Id);
        }
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

    /// <summary>
    /// Container name for a service: explicit <c>container_name:</c> when set, otherwise
    /// <c>&lt;project&gt;-&lt;service&gt;</c>.
    /// </summary>
    private static string ResolveContainerName(string projectName, ServiceSpec service)
        => string.IsNullOrWhiteSpace(service.ContainerName)
            ? WslccLabels.ContainerName(projectName, service.Name)
            : service.ContainerName!;

    /// <summary>
    /// Compose forbids two services sharing a <c>container_name</c> (or resolving to the same default name).
    /// </summary>
    private static void EnsureUniqueContainerNames(string projectName, ComposeFile file)
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var service in file.Services.Values)
        {
            var name = ResolveContainerName(projectName, service);
            if (owners.TryGetValue(name, out var other))
            {
                throw new ProviderException(
                    $"services '{other}' and '{service.Name}' both use container name '{name}'.");
            }

            owners[name] = service.Name;
        }
    }

    /// <summary>
    /// Translates a service into a provider-agnostic run spec. Networks and volume mounts are not part of
    /// this translation: they depend on the project's resolved resources and are applied by the caller.
    /// </summary>
    private static ContainerRunSpec ToRunSpec(
        string projectName,
        ServiceSpec service,
        string image,
        string? baseDirectory)
    {
        var spec = new ContainerRunSpec
        {
            Image = image,
            Name = ResolveContainerName(projectName, service),
            // Compose file — user / working_dir / hostname: process identity options.
            User = service.User,
            WorkingDir = service.WorkingDir,
            Hostname = service.Hostname,
            // Compose file — read_only: root filesystem --read-only.
            ReadOnly = service.ReadOnly,
            Restart = service.Restart,
            HealthCheck = BuildContainerHealthCheck(service.HealthCheck),
            Detach = true,
        };

        ApplyLabels(spec, projectName, service);
        ApplyAnnotations(spec, service);
        ApplyEnvironment(spec, service, baseDirectory);
        ApplyPortsAndProcess(spec, service);

        return spec;
    }

    /// <summary>
    /// Compose file — labels: the service's labels are copied first so WSLCC's own project/service/hash
    /// labels always win and stay reliable for discovery.
    /// </summary>
    private static void ApplyLabels(ContainerRunSpec spec, string projectName, ServiceSpec service)
    {
        foreach (var label in service.Labels)
            spec.Labels[label.Key] = label.Value;

        spec.Labels[WslccLabels.Project] = projectName;
        spec.Labels[WslccLabels.Service] = service.Name;
    }

    /// <summary>
    /// Compose file — annotations: OCI annotations are copied as-is. WSLCC identity stays on labels.
    /// </summary>
    private static void ApplyAnnotations(ContainerRunSpec spec, ServiceSpec service)
    {
        foreach (var annotation in service.Annotations)
            spec.Annotations[annotation.Key] = annotation.Value;
    }

    /// <summary>
    /// Compose file — env_file / environment: env files are resolved to host paths and passed to the
    /// runtime (which applies them before <c>environment:</c>, so inline keys win). A missing file is
    /// fatal only when the entry is <c>required</c>.
    /// </summary>
    private static void ApplyEnvironment(ContainerRunSpec spec, ServiceSpec service, string? baseDirectory)
    {
        foreach (var envFile in service.EnvFile)
        {
            if (string.IsNullOrWhiteSpace(envFile.Path))
                continue;

            var path = ResolveHostPath(envFile.Path, baseDirectory);
            if (!File.Exists(path))
            {
                if (envFile.Required)
                    throw new ProviderException($"service '{service.Name}': env_file not found: {path}");

                continue;
            }

            spec.EnvFiles.Add(path);
        }

        foreach (var env in service.Environment)
            spec.Environment[env.Key] = env.Value;
    }

    /// <summary>
    /// Compose file — ports / entrypoint / command: all three are already normalized by the parser
    /// (ports to short syntax, entrypoint and command to argv tokens), so they copy across verbatim.
    /// </summary>
    private static void ApplyPortsAndProcess(ContainerRunSpec spec, ServiceSpec service)
    {
        foreach (var port in service.Ports)
            spec.Ports.Add(port);

        foreach (var token in service.Entrypoint)
            spec.Entrypoint.Add(token);

        foreach (var token in service.Command)
            spec.Command.Add(token);
    }

    /// <summary>
    /// Resolves a host path against <paramref name="baseDirectory"/> when relative. Absolute paths
    /// (and paths with no base) are returned as-is.
    /// </summary>
    private static string ResolveHostPath(string path, string? baseDirectory)
    {
        if (Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(baseDirectory))
            return path;

        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    /// <summary>
    /// Translates a service's <c>healthcheck:</c> into the provider-agnostic <see cref="ContainerHealthCheck"/>.
    /// The Compose <c>test</c> array (<c>CMD-SHELL</c>/<c>CMD</c>/string short form) is flattened into a
    /// single shell command; <c>disable</c>/<c>NONE</c> becomes a disabled healthcheck.
    /// </summary>
    private static ContainerHealthCheck? BuildContainerHealthCheck(HealthCheckSpec? spec)
    {
        if (spec is null)
            return null;

        if (spec.Disabled)
            return new ContainerHealthCheck { Disabled = true };

        var command = ResolveHealthCommand(spec.Test);
        var hasTimings = spec.Interval is not null
            || spec.Timeout is not null
            || spec.Retries is not null
            || spec.StartPeriod is not null;

        // Nothing to apply beyond whatever the image already declares.
        if (command is null && !hasTimings)
            return null;

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
            return null;

        // Compose forms: ["CMD-SHELL", "<shell cmd>"], ["CMD", "<argv>", ...], or a string short form
        // (stored as a single element). The container CLI's --health-cmd runs via a shell either way.
        var isCmdForm = string.Equals(test[0], "CMD-SHELL", StringComparison.Ordinal)
            || string.Equals(test[0], "CMD", StringComparison.Ordinal);
        if (isCmdForm)
            return test.Count > 1 ? string.Join(" ", test.Skip(1)) : null;

        return string.Join(" ", test);
    }

    /// <summary>Depth-first topological ordering by <c>depends_on</c>, tolerant of cycles/missing deps.</summary>
    internal static IReadOnlyList<ServiceSpec> OrderServices(ComposeFile file)
    {
        var ordered = new List<ServiceSpec>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var inProgress = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in file.Services.Keys)
            VisitService(file, name, visited, inProgress, ordered);

        return ordered;
    }

    /// <summary>
    /// Appends <paramref name="name"/> to <paramref name="ordered"/> after everything it depends on.
    /// A dependency on an unknown service is ignored, and a cycle stops at its first repeated node
    /// instead of throwing — ordering is best-effort so a malformed graph still brings services up.
    /// </summary>
    private static void VisitService(
        ComposeFile file,
        string name,
        HashSet<string> visited,
        HashSet<string> inProgress,
        List<ServiceSpec> ordered)
    {
        if (visited.Contains(name))
            return;

        if (!file.Services.TryGetValue(name, out var service))
            return; // dependency on an unknown service; ignore

        if (!inProgress.Add(name))
            return; // cycle guard

        foreach (var dependency in service.DependsOn)
            VisitService(file, dependency.Name, visited, inProgress, ordered);

        inProgress.Remove(name);
        visited.Add(name);
        ordered.Add(service);
    }

    /// <summary>
    /// Validates the project name every project-scoped operation needs: a blank name would silently
    /// address the wrong (or every) project, since it is what containers are labelled and named after.
    /// </summary>
    private static void RequireProjectName(string projectName)
        => ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

    private IContainerProvider ResolveProvider(string? providerName)
    {
        if (_providers.Count == 0)
            throw new InvalidOperationException("No container providers are registered.");

        var requested = string.IsNullOrWhiteSpace(providerName) ? _defaultProvider : providerName;

        if (string.IsNullOrWhiteSpace(requested))
            return _providers[0];

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

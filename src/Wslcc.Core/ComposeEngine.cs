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
        CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(providerName);
        var results = new List<ServiceOperationResult>();

        foreach (var service in OrderServices(file))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var image = await PrepareServiceImageAsync(provider, projectName, service, baseDirectory, pull, buildPolicy, cancellationToken)
                    .ConfigureAwait(false);

                var spec = ToRunSpec(projectName, service, image);
                await TryRemoveExistingAsync(provider, spec.Name, cancellationToken).ConfigureAwait(false);

                var id = await provider.RunContainerAsync(spec, cancellationToken).ConfigureAwait(false);
                results.Add(new ServiceOperationResult(service.Name, "started", id));
            }
            catch (ProviderException ex)
            {
                results.Add(new ServiceOperationResult(service.Name, "failed", Error: ex.Message));
            }
        }

        return results;
    }

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

        return results;
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

            if (string.IsNullOrWhiteSpace(service.Image))
            {
                results.Add(new ServiceOperationResult(service.Name, "failed", Error: "no 'image' specified"));
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

        // Fan in: one pump task per container writes tagged lines into a shared channel so the caller
        // sees an interleaved stream, mirroring how `docker compose logs` merges multiple containers.
        var channel = Channel.CreateUnbounded<ServiceLogLine>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var pumpTasks = containers
            .Select(container => PumpLogsAsync(provider, container, channel.Writer, follow, tail, cancellationToken))
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

    private static async Task PumpLogsAsync(
        IContainerProvider provider,
        ContainerInfo container,
        ChannelWriter<ServiceLogLine> writer,
        bool follow,
        int? tail,
        CancellationToken cancellationToken)
    {
        var serviceName = container.Service ?? container.Name;

        try
        {
            await foreach (var line in provider.GetLogsAsync(container.Name, follow, tail, cancellationToken).ConfigureAwait(false))
            {
                await writer.WriteAsync(new ServiceLogLine(serviceName, line), cancellationToken).ConfigureAwait(false);
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
                Visit(dependency);
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

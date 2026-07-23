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
        CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(providerName);
        var results = new List<ServiceOperationResult>();

        foreach (var service in OrderServices(file))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(service.Image))
            {
                var reason = service.Build is not null
                    ? "building images is not supported yet; specify an 'image'"
                    : "no 'image' specified";
                results.Add(new ServiceOperationResult(service.Name, "failed", Error: reason));
                continue;
            }

            try
            {
                await provider.EnsureImageAsync(service.Image!, pull, cancellationToken).ConfigureAwait(false);

                var spec = ToRunSpec(projectName, service);
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

    public async Task<IReadOnlyList<ServiceOperationResult>> DownAsync(
        string projectName,
        string? providerName,
        CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(providerName);
        var containers = await provider.ListContainersAsync(projectName, all: true, cancellationToken).ConfigureAwait(false);

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
        string? providerName,
        IReadOnlyList<string>? services,
        CancellationToken cancellationToken = default)
        => ApplyToContainersAsync(
            projectName, providerName, services, "started",
            (provider, containerName, ct) => provider.StartContainerAsync(containerName, ct),
            cancellationToken);

    public Task<IReadOnlyList<ServiceOperationResult>> StopAsync(
        string projectName,
        string? providerName,
        IReadOnlyList<string>? services,
        CancellationToken cancellationToken = default)
        => ApplyToContainersAsync(
            projectName, providerName, services, "stopped",
            (provider, containerName, ct) => provider.StopContainerAsync(containerName, ct),
            cancellationToken);

    public Task<IReadOnlyList<ServiceOperationResult>> RestartAsync(
        string projectName,
        string? providerName,
        IReadOnlyList<string>? services,
        CancellationToken cancellationToken = default)
        => ApplyToContainersAsync(
            projectName, providerName, services, "restarted",
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

            var context = ResolveBuildContext(service.Build.Context, baseDirectory);
            if (context is null)
            {
                results.Add(new ServiceOperationResult(service.Name, "failed", Error: "'build' has no context"));
                continue;
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

    /// <summary>Selects the requested services from the file, or every service when none were requested.</summary>
    private static IEnumerable<ServiceSpec> SelectServices(ComposeFile file, IReadOnlyList<string>? services)
    {
        if (services is not { Count: > 0 })
        {
            return file.Services.Values;
        }

        var selected = new List<ServiceSpec>();
        foreach (var name in services)
        {
            if (file.Services.TryGetValue(name, out var service))
            {
                selected.Add(service);
            }
        }

        return selected;
    }

    public async IAsyncEnumerable<ServiceLogLine> GetLogsAsync(
        string projectName,
        string? providerName,
        IReadOnlyList<string>? services,
        bool follow,
        int? tail,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(providerName);
        var containers = await provider.ListContainersAsync(projectName, all: true, cancellationToken).ConfigureAwait(false);

        if (services is { Count: > 0 })
        {
            var requested = new HashSet<string>(services, StringComparer.Ordinal);
            containers = containers.Where(c => c.Service is not null && requested.Contains(c.Service)).ToList();
        }

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
    /// Shared driver for start/stop/restart: lists the project's existing containers, optionally
    /// filters to the requested service names, then applies <paramref name="action"/> to each,
    /// capturing a per-service outcome instead of throwing on the first failure.
    /// </summary>
    private async Task<IReadOnlyList<ServiceOperationResult>> ApplyToContainersAsync(
        string projectName,
        string? providerName,
        IReadOnlyList<string>? services,
        string successStatus,
        Func<IContainerProvider, string, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providerName);
        var containers = await provider.ListContainersAsync(projectName, all: true, cancellationToken).ConfigureAwait(false);

        if (services is { Count: > 0 })
        {
            var requested = new HashSet<string>(services, StringComparer.Ordinal);
            containers = containers.Where(c => c.Service is not null && requested.Contains(c.Service)).ToList();
        }

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

    private static ContainerRunSpec ToRunSpec(string projectName, ServiceSpec service)
    {
        var spec = new ContainerRunSpec
        {
            Image = service.Image ?? string.Empty,
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

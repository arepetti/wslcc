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

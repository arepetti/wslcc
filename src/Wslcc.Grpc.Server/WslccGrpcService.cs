using Grpc.Core;
using Wslcc.Abstractions;
using Wslcc.Abstractions.Compose;
using Wslcc.Core;
using Wslcc.Core.Compose;
using Wslcc.Grpc.Contracts;

namespace Wslcc.Grpc.Server;

/// <summary>
/// gRPC service implementation. Translates RPCs into calls on the <see cref="IComposeEngine"/>.
/// </summary>
public sealed class WslccGrpcService : global::Wslcc.Grpc.Contracts.Wslcc.WslccBase
{
    private readonly IComposeEngine _engine;
    private readonly IDaemonLifetime _lifetime;
    private readonly WslccServerOptions _options;

    public WslccGrpcService(IComposeEngine engine, IDaemonLifetime lifetime, WslccServerOptions options)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public override Task<PingResponse> Ping(PingRequest request, ServerCallContext context)
        => Task.FromResult(new PingResponse
        {
            DaemonVersion = _options.DaemonVersion,
            DefaultProvider = _options.DefaultProvider,
        });

    public override async Task<GetVersionResponse> GetVersion(GetVersionRequest request, ServerCallContext context)
    {
        var response = new GetVersionResponse
        {
            DaemonVersion = _options.DaemonVersion,
        };

        IReadOnlyList<ProviderInfo> infos;
        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            infos = await _engine.GetProviderInfosAsync(context.CancellationToken).ConfigureAwait(false);
        }
        else
        {
            var single = await _engine.GetProviderInfoAsync(request.Provider, context.CancellationToken)
                .ConfigureAwait(false);
            infos = new[] { single };
        }

        foreach (var info in infos)
        {
            response.Providers.Add(new ComponentVersion
            {
                Name = info.Name,
                DisplayName = info.DisplayName,
                Available = info.IsAvailable,
                Version = info.Version ?? string.Empty,
                Details = info.Details ?? string.Empty,
            });
        }

        return response;
    }

    public override Task<ShutdownResponse> Shutdown(ShutdownRequest request, ServerCallContext context)
    {
        _lifetime.RequestShutdown();
        return Task.FromResult(new ShutdownResponse { Accepted = true });
    }

    public override async Task<UpResponse> Up(UpRequest request, ServerCallContext context)
    {
        var file = Parse(request.ComposeYaml);
        if (file is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "No compose file content was provided."));
        }

        var project = ProjectNames.Resolve(request.ProjectName, file, request.DefaultProjectName);

        var results = await Guard(() =>
            _engine.UpAsync(project, file, NullIfEmpty(request.Provider), request.Pull, context.CancellationToken))
            .ConfigureAwait(false);

        var response = new UpResponse { ProjectName = project };
        response.Results.AddRange(results.Select(ToServiceResult));
        return response;
    }

    public override async Task<DownResponse> Down(DownRequest request, ServerCallContext context)
    {
        var file = Parse(request.ComposeYaml);
        var project = ProjectNames.ResolveOrNull(request.ProjectName, file, request.DefaultProjectName);
        if (project is null)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "No project specified. Provide a compose file (-f) or a project name (-p)."));
        }

        var results = await Guard(() =>
            _engine.DownAsync(project, NullIfEmpty(request.Provider), context.CancellationToken))
            .ConfigureAwait(false);

        var response = new DownResponse { ProjectName = project };
        response.Results.AddRange(results.Select(ToServiceResult));
        return response;
    }

    public override async Task<PsResponse> Ps(PsRequest request, ServerCallContext context)
    {
        var file = Parse(request.ComposeYaml);
        // Null project means "list every wslcc-managed container, across all projects".
        var project = ProjectNames.ResolveOrNull(request.ProjectName, file, request.DefaultProjectName);

        var containers = await Guard(() =>
            _engine.PsAsync(project, NullIfEmpty(request.Provider), request.All, context.CancellationToken))
            .ConfigureAwait(false);

        var response = new PsResponse { ProjectName = project ?? string.Empty };
        response.Containers.AddRange(containers.Select(ToContainer));
        return response;
    }

    /// <summary>
    /// Runs an engine call and translates domain failures into meaningful gRPC statuses so the CLI can
    /// show the underlying reason (e.g. "docker engine not running") instead of an opaque "Unknown".
    /// </summary>
    private static async Task<T> Guard<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (ProviderException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
    }

    private static ComposeFile? Parse(string? yaml)
        => string.IsNullOrWhiteSpace(yaml) ? null : new ComposeFileParser().Parse(yaml!);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static ServiceResult ToServiceResult(ServiceOperationResult result) => new()
    {
        Service = result.Service,
        ContainerId = result.ContainerId ?? string.Empty,
        Status = result.Status,
        Error = result.Error ?? string.Empty,
    };

    private static Container ToContainer(ContainerInfo info) => new()
    {
        Id = info.Id,
        Name = info.Name,
        Image = info.Image,
        State = info.State,
        Status = info.Status ?? string.Empty,
        Ports = info.Ports ?? string.Empty,
        Service = info.Service ?? string.Empty,
        Project = info.Project ?? string.Empty,
    };
}

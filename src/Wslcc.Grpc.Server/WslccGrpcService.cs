using System.Globalization;
using Grpc.Core;
using Wslcc.Abstractions;
using Wslcc.Abstractions.Compose;
using Wslcc.Compose;
using Wslcc.Core;
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

        var buildPolicy = request.BuildPolicy switch
        {
            global::Wslcc.Grpc.Contracts.BuildPolicy.Always => global::Wslcc.Abstractions.BuildPolicy.Always,
            global::Wslcc.Grpc.Contracts.BuildPolicy.Never => global::Wslcc.Abstractions.BuildPolicy.Never,
            _ => global::Wslcc.Abstractions.BuildPolicy.Auto,
        };

        // The per-service config hash (identical to `config --hash`) lets the engine leave unchanged,
        // still-running containers in place instead of recreating them.
        var configHashes = ComposeHash.ComputeServiceHashes(request.ComposeYaml);

        var results = await Guard(() =>
            _engine.UpAsync(
                project, file, NullIfEmpty(request.Provider), request.Pull, buildPolicy, NullIfEmpty(request.BaseDirectory),
                configHashes, context.CancellationToken))
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
            _engine.DownAsync(project, file, NullIfEmpty(request.Provider), context.CancellationToken))
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

    public override async Task<StartResponse> Start(StartRequest request, ServerCallContext context)
    {
        var file = Parse(request.ComposeYaml);
        var project = ProjectNames.ResolveOrNull(request.ProjectName, file, request.DefaultProjectName);
        if (project is null)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "No project specified. Provide a compose file (-f) or a project name (-p)."));
        }

        var services = request.Services.Count > 0 ? request.Services.ToList() : null;
        var results = await Guard(() =>
            _engine.StartAsync(project, file, NullIfEmpty(request.Provider), services, context.CancellationToken))
            .ConfigureAwait(false);

        var response = new StartResponse { ProjectName = project };
        response.Results.AddRange(results.Select(ToServiceResult));
        return response;
    }

    public override async Task<StopResponse> Stop(StopRequest request, ServerCallContext context)
    {
        var file = Parse(request.ComposeYaml);
        var project = ProjectNames.ResolveOrNull(request.ProjectName, file, request.DefaultProjectName);
        if (project is null)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "No project specified. Provide a compose file (-f) or a project name (-p)."));
        }

        var services = request.Services.Count > 0 ? request.Services.ToList() : null;
        var results = await Guard(() =>
            _engine.StopAsync(project, file, NullIfEmpty(request.Provider), services, context.CancellationToken))
            .ConfigureAwait(false);

        var response = new StopResponse { ProjectName = project };
        response.Results.AddRange(results.Select(ToServiceResult));
        return response;
    }

    public override async Task<RestartResponse> Restart(RestartRequest request, ServerCallContext context)
    {
        var file = Parse(request.ComposeYaml);
        var project = ProjectNames.ResolveOrNull(request.ProjectName, file, request.DefaultProjectName);
        if (project is null)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "No project specified. Provide a compose file (-f) or a project name (-p)."));
        }

        var services = request.Services.Count > 0 ? request.Services.ToList() : null;
        var results = await Guard(() =>
            _engine.RestartAsync(project, file, NullIfEmpty(request.Provider), services, context.CancellationToken))
            .ConfigureAwait(false);

        var response = new RestartResponse { ProjectName = project };
        response.Results.AddRange(results.Select(ToServiceResult));
        return response;
    }

    public override async Task<PullResponse> Pull(PullRequest request, ServerCallContext context)
    {
        var file = Parse(request.ComposeYaml);
        if (file is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "No compose file content was provided."));
        }

        var project = ProjectNames.Resolve(request.ProjectName, file, request.DefaultProjectName);
        var services = request.Services.Count > 0 ? request.Services.ToList() : null;

        var results = await Guard(() =>
            _engine.PullAsync(file, NullIfEmpty(request.Provider), services, context.CancellationToken))
            .ConfigureAwait(false);

        var response = new PullResponse { ProjectName = project };
        response.Results.AddRange(results.Select(ToServiceResult));
        return response;
    }

    public override async Task<BuildResponse> Build(BuildRequest request, ServerCallContext context)
    {
        var file = Parse(request.ComposeYaml);
        if (file is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "No compose file content was provided."));
        }

        var project = ProjectNames.Resolve(request.ProjectName, file, request.DefaultProjectName);
        var services = request.Services.Count > 0 ? request.Services.ToList() : null;

        var results = await Guard(() =>
            _engine.BuildAsync(
                project, file, NullIfEmpty(request.Provider), NullIfEmpty(request.BaseDirectory), services, context.CancellationToken))
            .ConfigureAwait(false);

        var response = new BuildResponse { ProjectName = project };
        response.Results.AddRange(results.Select(ToServiceResult));
        return response;
    }

    public override async Task Logs(
        LogsRequest request,
        IServerStreamWriter<LogLine> responseStream,
        ServerCallContext context)
    {
        var file = Parse(request.ComposeYaml);
        var project = ProjectNames.ResolveOrNull(request.ProjectName, file, request.DefaultProjectName);
        if (project is null)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "No project specified. Provide a compose file (-f) or a project name (-p)."));
        }

        var services = request.Services.Count > 0 ? request.Services.ToList() : null;
        int? tail = request.HasTail ? request.Tail : null;

        try
        {
            var lines = _engine.GetLogsAsync(
                project, file, NullIfEmpty(request.Provider), services,
                request.Follow, tail, request.Timestamps, NullIfEmpty(request.Since), context.CancellationToken);

            await foreach (var line in lines.WithCancellation(context.CancellationToken).ConfigureAwait(false))
            {
                var message = new LogLine { Service = line.Service, Line = line.Line };
                if (request.Timestamps && line.Timestamp is { } ts)
                {
                    message.Timestamp = ts.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
                }

                await responseStream.WriteAsync(message).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // The client stopped following (e.g. Ctrl+C) or disconnected; nothing more to send.
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

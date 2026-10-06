using System.Globalization;
using Grpc.Core;
using Wslcc.Abstractions;
using Wslcc.Abstractions.Compose;
using Wslcc.Compose.Configuration;
using Wslcc.Compose.Engine;
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
    infos = await _engine.GetProviderInfosAsync(context.CancellationToken).ConfigureAwait(false);
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

    public override Task Up(UpRequest request, IServerStreamWriter<UpEvent> responseStream, ServerCallContext context)
        => StreamGuard(async () =>
        {
            var file = RequireComposeFile(request.ComposeYaml);
            var project = ProjectNames.Resolve(request.ProjectName, file, request.DefaultProjectName);
            var buildPolicy = request.BuildPolicy switch
            {
                global::Wslcc.Grpc.Contracts.BuildPolicy.Always => global::Wslcc.Abstractions.BuildPolicy.Always,
                global::Wslcc.Grpc.Contracts.BuildPolicy.Never => global::Wslcc.Abstractions.BuildPolicy.Never,
                _ => global::Wslcc.Abstractions.BuildPolicy.Auto,
            };
            var configHashes = ComposeHash.ComputeServiceHashes(request.ComposeYaml);

            await ProgressStream.WriteOperationAsync(
                responseStream,
                (progress, ct) => _engine.UpAsync(
                    project, file, NullIfEmpty(request.Provider), request.Pull, buildPolicy,
                    NullIfEmpty(request.BaseDirectory), configHashes, progress, ct),
                p => new UpEvent { Progress = p },
                results =>
                {
                    var response = new UpResponse { ProjectName = project };
                    response.Results.AddRange(results.Select(ToServiceResult));
                    return new UpEvent { Completed = response };
                },
                context).ConfigureAwait(false);
        });

    public override Task Down(DownRequest request, IServerStreamWriter<DownEvent> responseStream, ServerCallContext context)
        => StreamGuard(async () =>
        {
            var file = Parse(request.ComposeYaml);
            var project = RequireProject(request.ProjectName, file, request.DefaultProjectName);

            await ProgressStream.WriteOperationAsync(
                responseStream,
                (progress, ct) => _engine.DownAsync(
                    project, file, NullIfEmpty(request.Provider), request.Volumes, progress, ct),
                p => new DownEvent { Progress = p },
                results =>
                {
                    var response = new DownResponse { ProjectName = project };
                    response.Results.AddRange(results.Select(ToServiceResult));
                    return new DownEvent { Completed = response };
                },
                context).ConfigureAwait(false);
        });

    public override Task Ps(PsRequest request, IServerStreamWriter<PsEvent> responseStream, ServerCallContext context)
        => StreamGuard(async () =>
        {
            var file = Parse(request.ComposeYaml);
            var project = ProjectNames.ResolveOrNull(request.ProjectName, file, request.DefaultProjectName);

            await ProgressStream.WriteOperationAsync(
                responseStream,
                (progress, ct) => _engine.PsAsync(project, NullIfEmpty(request.Provider), request.All, progress, ct),
                p => new PsEvent { Progress = p },
                containers =>
                {
                    var response = new PsResponse { ProjectName = project ?? string.Empty };
                    response.Containers.AddRange(containers.Select(ToContainer));
                    return new PsEvent { Completed = response };
                },
                context).ConfigureAwait(false);
        });

    public override Task Start(StartRequest request, IServerStreamWriter<StartEvent> responseStream, ServerCallContext context)
        => StreamGuard(async () =>
        {
            var file = Parse(request.ComposeYaml);
            var project = RequireProject(request.ProjectName, file, request.DefaultProjectName);
            var services = request.Services.Count > 0 ? request.Services.ToList() : null;

            await ProgressStream.WriteOperationAsync(
                responseStream,
                (progress, ct) => _engine.StartAsync(
                    project, file, NullIfEmpty(request.Provider), services, progress, ct),
                p => new StartEvent { Progress = p },
                results =>
                {
                    var response = new StartResponse { ProjectName = project };
                    response.Results.AddRange(results.Select(ToServiceResult));
                    return new StartEvent { Completed = response };
                },
                context).ConfigureAwait(false);
        });

    public override Task Stop(StopRequest request, IServerStreamWriter<StopEvent> responseStream, ServerCallContext context)
        => StreamGuard(async () =>
        {
            var file = Parse(request.ComposeYaml);
            var project = RequireProject(request.ProjectName, file, request.DefaultProjectName);
            var services = request.Services.Count > 0 ? request.Services.ToList() : null;

            await ProgressStream.WriteOperationAsync(
                responseStream,
                (progress, ct) => _engine.StopAsync(
                    project, file, NullIfEmpty(request.Provider), services, progress, ct),
                p => new StopEvent { Progress = p },
                results =>
                {
                    var response = new StopResponse { ProjectName = project };
                    response.Results.AddRange(results.Select(ToServiceResult));
                    return new StopEvent { Completed = response };
                },
                context).ConfigureAwait(false);
        });

    public override Task Restart(RestartRequest request, IServerStreamWriter<RestartEvent> responseStream, ServerCallContext context)
        => StreamGuard(async () =>
        {
            var file = Parse(request.ComposeYaml);
            var project = RequireProject(request.ProjectName, file, request.DefaultProjectName);
            var services = request.Services.Count > 0 ? request.Services.ToList() : null;

            await ProgressStream.WriteOperationAsync(
                responseStream,
                (progress, ct) => _engine.RestartAsync(
                    project, file, NullIfEmpty(request.Provider), services, progress, ct),
                p => new RestartEvent { Progress = p },
                results =>
                {
                    var response = new RestartResponse { ProjectName = project };
                    response.Results.AddRange(results.Select(ToServiceResult));
                    return new RestartEvent { Completed = response };
                },
                context).ConfigureAwait(false);
        });

    public override Task Pull(PullRequest request, IServerStreamWriter<PullEvent> responseStream, ServerCallContext context)
        => StreamGuard(async () =>
        {
            var file = RequireComposeFile(request.ComposeYaml);
            var project = ProjectNames.Resolve(request.ProjectName, file, request.DefaultProjectName);
            var services = request.Services.Count > 0 ? request.Services.ToList() : null;

            await ProgressStream.WriteOperationAsync(
                responseStream,
                (progress, ct) => _engine.PullAsync(file, NullIfEmpty(request.Provider), services, progress, ct),
                p => new PullEvent { Progress = p },
                results =>
                {
                    var response = new PullResponse { ProjectName = project };
                    response.Results.AddRange(results.Select(ToServiceResult));
                    return new PullEvent { Completed = response };
                },
                context).ConfigureAwait(false);
        });

    public override Task Build(BuildRequest request, IServerStreamWriter<BuildEvent> responseStream, ServerCallContext context)
        => StreamGuard(async () =>
        {
            var file = RequireComposeFile(request.ComposeYaml);
            var project = ProjectNames.Resolve(request.ProjectName, file, request.DefaultProjectName);
            var services = request.Services.Count > 0 ? request.Services.ToList() : null;

            await ProgressStream.WriteOperationAsync(
                responseStream,
                (progress, ct) => _engine.BuildAsync(
                    project, file, NullIfEmpty(request.Provider), NullIfEmpty(request.BaseDirectory), services, progress, ct),
                p => new BuildEvent { Progress = p },
                results =>
                {
                    var response = new BuildResponse { ProjectName = project };
                    response.Results.AddRange(results.Select(ToServiceResult));
                    return new BuildEvent { Completed = response };
                },
                context).ConfigureAwait(false);
        });

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
    message.Timestamp = ts.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);

                await responseStream.WriteAsync(message).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // The client stopped following (e.g. Ctrl+C) or disconnected; nothing more to send.
        }
        catch (ComposeLoadException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
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

    private static async Task StreamGuard(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (ComposeLoadException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (ProviderException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
        catch (RpcException)
        {
            throw;
        }
    }

    private static ComposeFile RequireComposeFile(string? yaml)
    {
        var file = Parse(yaml);
        if (file is null)
    throw new RpcException(new Status(StatusCode.InvalidArgument, "No compose file content was provided."));

        return file;
    }

    private static string RequireProject(string projectName, ComposeFile? file, string defaultProjectName)
    {
        var project = ProjectNames.ResolveOrNull(projectName, file, defaultProjectName);
        if (project is null)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "No project specified. Provide a compose file (-f) or a project name (-p)."));
        }

        return project;
    }

    private static ComposeFile? Parse(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
    return null;

        try
        {
            return new ComposeFileParser().Parse(yaml!);
        }
        catch (ComposeLoadException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }

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

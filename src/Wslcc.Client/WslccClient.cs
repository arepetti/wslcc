using System.Net.Http;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Grpc.Net.Client;
using Wslcc.Grpc.Contracts;

namespace Wslcc.Client;

/// <summary>
/// Typed client over the WSLCC gRPC service. Hides the transport (named pipe vs HTTP/2) behind a
/// simple async API, and is shared by the CLI and (later) the GUI.
/// </summary>
public sealed class WslccClient : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly global::Wslcc.Grpc.Contracts.Wslcc.WslccClient _client;

    public WslccClient(WslccEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        Endpoint = endpoint;
        _channel = CreateChannel(endpoint);
        _client = new global::Wslcc.Grpc.Contracts.Wslcc.WslccClient(_channel);
    }

    public WslccClient(string? host)
        : this(WslccEndpoint.Parse(host))
    {
    }

    public WslccEndpoint Endpoint { get; }

    public async Task<PingResponse> PingAsync(CancellationToken cancellationToken = default)
    {
        return await _client.PingAsync(new PingRequest(), cancellationToken: cancellationToken);
    }

    public async Task<GetVersionResponse> GetVersionAsync(
        string? provider = null,
        CancellationToken cancellationToken = default)
    {
        var request = new GetVersionRequest { Provider = provider ?? string.Empty };
        return await _client.GetVersionAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<ShutdownResponse> ShutdownAsync(CancellationToken cancellationToken = default)
    {
        return await _client.ShutdownAsync(new ShutdownRequest(), cancellationToken: cancellationToken);
    }

    public Task<UpResponse> UpAsync(
        UpRequest request,
        IProgress<ServiceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return CollectAsync(
            () => _client.Up(request, cancellationToken: cancellationToken),
            ev => ev.PayloadCase == UpEvent.PayloadOneofCase.Progress ? ev.Progress : null,
            ev => ev.PayloadCase == UpEvent.PayloadOneofCase.Completed ? ev.Completed : null,
            progress,
            cancellationToken);
    }

    public Task<DownResponse> DownAsync(
        DownRequest request,
        IProgress<ServiceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return CollectAsync(
            () => _client.Down(request, cancellationToken: cancellationToken),
            ev => ev.PayloadCase == DownEvent.PayloadOneofCase.Progress ? ev.Progress : null,
            ev => ev.PayloadCase == DownEvent.PayloadOneofCase.Completed ? ev.Completed : null,
            progress,
            cancellationToken);
    }

    public Task<PsResponse> PsAsync(
        PsRequest request,
        IProgress<ServiceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return CollectAsync(
            () => _client.Ps(request, cancellationToken: cancellationToken),
            ev => ev.PayloadCase == PsEvent.PayloadOneofCase.Progress ? ev.Progress : null,
            ev => ev.PayloadCase == PsEvent.PayloadOneofCase.Completed ? ev.Completed : null,
            progress,
            cancellationToken);
    }

    public Task<StartResponse> StartAsync(
        StartRequest request,
        IProgress<ServiceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return CollectAsync(
            () => _client.Start(request, cancellationToken: cancellationToken),
            ev => ev.PayloadCase == StartEvent.PayloadOneofCase.Progress ? ev.Progress : null,
            ev => ev.PayloadCase == StartEvent.PayloadOneofCase.Completed ? ev.Completed : null,
            progress,
            cancellationToken);
    }

    public Task<StopResponse> StopAsync(
        StopRequest request,
        IProgress<ServiceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return CollectAsync(
            () => _client.Stop(request, cancellationToken: cancellationToken),
            ev => ev.PayloadCase == StopEvent.PayloadOneofCase.Progress ? ev.Progress : null,
            ev => ev.PayloadCase == StopEvent.PayloadOneofCase.Completed ? ev.Completed : null,
            progress,
            cancellationToken);
    }

    public Task<RestartResponse> RestartAsync(
        RestartRequest request,
        IProgress<ServiceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return CollectAsync(
            () => _client.Restart(request, cancellationToken: cancellationToken),
            ev => ev.PayloadCase == RestartEvent.PayloadOneofCase.Progress ? ev.Progress : null,
            ev => ev.PayloadCase == RestartEvent.PayloadOneofCase.Completed ? ev.Completed : null,
            progress,
            cancellationToken);
    }

    public Task<PullResponse> PullAsync(
        PullRequest request,
        IProgress<ServiceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return CollectAsync(
            () => _client.Pull(request, cancellationToken: cancellationToken),
            ev => ev.PayloadCase == PullEvent.PayloadOneofCase.Progress ? ev.Progress : null,
            ev => ev.PayloadCase == PullEvent.PayloadOneofCase.Completed ? ev.Completed : null,
            progress,
            cancellationToken);
    }

    public Task<BuildResponse> BuildAsync(
        BuildRequest request,
        IProgress<ServiceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return CollectAsync(
            () => _client.Build(request, cancellationToken: cancellationToken),
            ev => ev.PayloadCase == BuildEvent.PayloadOneofCase.Progress ? ev.Progress : null,
            ev => ev.PayloadCase == BuildEvent.PayloadOneofCase.Completed ? ev.Completed : null,
            progress,
            cancellationToken);
    }

    /// <summary>Streams log lines from the server. Cancel <paramref name="cancellationToken"/> to stop following.</summary>
    public IAsyncEnumerable<LogLine> GetLogsAsync(
        LogsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return GetLogsCoreAsync(request, cancellationToken);
    }

    private async IAsyncEnumerable<LogLine> GetLogsCoreAsync(
        LogsRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = _client.Logs(request, cancellationToken: cancellationToken);
        await foreach (var line in call.ResponseStream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return line;
        }
    }

    public void Dispose() => _channel.Dispose();

    private static async Task<TResponse> CollectAsync<TEvent, TResponse>(
        Func<AsyncServerStreamingCall<TEvent>> start,
        Func<TEvent, ServiceProgress?> progressOf,
        Func<TEvent, TResponse?> completedOf,
        IProgress<ServiceProgress>? progress,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        using var call = start();
        await foreach (var ev in call.ResponseStream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var update = progressOf(ev);
            if (update is not null)
            {
                progress?.Report(update);
                continue;
            }

            var completed = completedOf(ev);
            if (completed is not null)
                return completed;
        }

        throw new InvalidOperationException("Lifecycle stream ended without a completed response.");
    }

    private static GrpcChannel CreateChannel(WslccEndpoint endpoint)
    {
        if (endpoint.IsNamedPipe)
        {
            var factory = new NamedPipeConnectionFactory(endpoint.ServerName, endpoint.PipeName);
            var handler = new SocketsHttpHandler
            {
                ConnectCallback = factory.ConnectAsync,
                EnableMultipleHttp2Connections = true,
            };

            return GrpcChannel.ForAddress(
                "http://localhost",
                new GrpcChannelOptions { HttpHandler = handler });
        }

        return GrpcChannel.ForAddress(endpoint.HttpUri!);
    }
}

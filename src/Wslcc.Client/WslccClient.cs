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
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
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

    public async Task<UpResponse> UpAsync(UpRequest request, CancellationToken cancellationToken = default)
    {
        return await _client.UpAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<DownResponse> DownAsync(DownRequest request, CancellationToken cancellationToken = default)
    {
        return await _client.DownAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<PsResponse> PsAsync(PsRequest request, CancellationToken cancellationToken = default)
    {
        return await _client.PsAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<StartResponse> StartAsync(StartRequest request, CancellationToken cancellationToken = default)
    {
        return await _client.StartAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<StopResponse> StopAsync(StopRequest request, CancellationToken cancellationToken = default)
    {
        return await _client.StopAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<RestartResponse> RestartAsync(RestartRequest request, CancellationToken cancellationToken = default)
    {
        return await _client.RestartAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<PullResponse> PullAsync(PullRequest request, CancellationToken cancellationToken = default)
    {
        return await _client.PullAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<BuildResponse> BuildAsync(BuildRequest request, CancellationToken cancellationToken = default)
    {
        return await _client.BuildAsync(request, cancellationToken: cancellationToken);
    }

    /// <summary>Streams log lines from the server. Cancel <paramref name="cancellationToken"/> to stop following.</summary>
    public async IAsyncEnumerable<LogLine> GetLogsAsync(
        LogsRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var call = _client.Logs(request, cancellationToken: cancellationToken);
        await foreach (var line in call.ResponseStream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return line;
        }
    }

    public void Dispose() => _channel.Dispose();

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

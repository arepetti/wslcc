using System.IO.Pipes;
using System.Net.Http;
using System.Security.Principal;

namespace Wslcc.Client;

/// <summary>
/// Provides a <see cref="SocketsHttpHandler.ConnectCallback"/> that connects a gRPC channel to a
/// Windows named pipe instead of a TCP socket.
/// </summary>
internal sealed class NamedPipeConnectionFactory
{
    private readonly string _serverName;
    private readonly string _pipeName;

    public NamedPipeConnectionFactory(string serverName, string pipeName)
    {
        _serverName = string.IsNullOrEmpty(serverName) ? "." : serverName;
        _pipeName = pipeName;
    }

    public async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        // Fast-fail when the daemon isn't running: connecting to a missing local pipe otherwise blocks
        // until the caller's timeout, which then surfaces as an opaque cancellation.
        if (_serverName == "." && !LocalPipeExists(_pipeName))
        {
            throw new TimeoutException($"Named pipe '{_pipeName}' is not available (is the daemon running?).");
        }

        var clientStream = new NamedPipeClientStream(
            _serverName,
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.WriteThrough | PipeOptions.Asynchronous,
            TokenImpersonationLevel.Anonymous);

        try
        {
            await clientStream.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return clientStream;
        }
        catch
        {
            await clientStream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static bool LocalPipeExists(string pipeName)
    {
        try
        {
            return File.Exists($@"\\.\pipe\{pipeName}");
        }
        catch
        {
            return false;
        }
    }
}

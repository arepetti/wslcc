using System.Net.Http;
using Grpc.Core;
using Wslcc.Client;
using Wslcc.Grpc.Contracts;

namespace Wslcc.Cli;

/// <summary>Helpers for talking to the daemon and classifying connection failures.</summary>
internal static class DaemonClientHelper
{
    /// <summary>
    /// Fast readiness check. Returns the daemon version, or <c>null</c> when unreachable. Does not
    /// touch providers, so it is safe to poll.
    /// </summary>
    public static async Task<PingResponse?> TryPingAsync(
        string? host,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(2));

        try
        {
            using var client = new WslccClient(host);
            return await client.PingAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsUnreachable(ex, cancellationToken))
        {
            DebugWrite(ex);
            return null;
        }
    }

    /// <summary>
    /// Attempts a <c>GetVersion</c> call (which resolves provider versions and can be slow when it
    /// shells out to docker/wslc). Returns <c>null</c> when the daemon is not reachable.
    /// </summary>
    public static async Task<GetVersionResponse?> TryGetVersionAsync(
        string? host,
        string? provider = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));

        try
        {
            using var client = new WslccClient(host);
            return await client.GetVersionAsync(provider, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsUnreachable(ex, cancellationToken))
        {
            DebugWrite(ex);
            return null;
        }
    }

    private static void DebugWrite(Exception ex)
    {
        if (Environment.GetEnvironmentVariable("WSLCC_DEBUG") == "1")
        {
            Console.Error.WriteLine(ex);
        }
    }

    /// <summary>
    /// True when a probe should be treated as "daemon not reachable" and swallowed (returning null).
    /// This includes transport failures and cancellation caused by our own probe timeout, but NOT a
    /// genuine user cancellation (Ctrl+C), which must propagate.
    /// </summary>
    private static bool IsUnreachable(Exception ex, CancellationToken userToken)
    {
        // Genuine user cancellation (Ctrl+C) must propagate; anything else during a best-effort probe
        // means "not reachable".
        if (userToken.IsCancellationRequested)
        {
            return false;
        }

        return ex is RpcException { StatusCode: StatusCode.Cancelled } || IsConnectionError(ex);
    }

    /// <summary>
    /// True when the failure indicates the daemon could not be reached (transport/connect failure) as
    /// opposed to a handler that ran and failed. Connect failures are wrapped by Grpc.Net.Client (often
    /// as <see cref="StatusCode.Internal"/>) with the real cause in <see cref="Status.DebugException"/>.
    /// </summary>
    public static bool IsConnectionError(Exception ex)
        => ex switch
        {
            RpcException rpc =>
                rpc.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded
                || (rpc.Status.DebugException is not null && IsConnectionError(rpc.Status.DebugException)),
            OperationCanceledException => true,
            TimeoutException => true,
            IOException => true,
            HttpRequestException => true,
            System.Net.Sockets.SocketException => true,
            _ => ex.InnerException is not null && IsConnectionError(ex.InnerException),
        };
}

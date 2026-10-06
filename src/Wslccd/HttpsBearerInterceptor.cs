using System.Security.Cryptography;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Wslccd;

/// <summary>
/// Requires <c>Authorization: Bearer</c> on HTTPS (TCP) calls. Named-pipe traffic is not HTTPS and
/// stays authenticated only by the pipe ACL.
/// </summary>
public sealed class HttpsBearerInterceptor : Interceptor
{
    public const string MetadataKey = "authorization";

    private readonly string _token;

    public HttpsBearerInterceptor(string token)
        => _token = token ?? string.Empty;

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        EnsureAuthorized(context);
        return base.UnaryServerHandler(request, context, continuation);
    }

    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnsureAuthorized(context);
        return base.ClientStreamingServerHandler(requestStream, context, continuation);
    }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnsureAuthorized(context);
        return base.ServerStreamingServerHandler(request, responseStream, context, continuation);
    }

    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnsureAuthorized(context);
        return base.DuplexStreamingServerHandler(requestStream, responseStream, context, continuation);
    }

    internal void EnsureAuthorized(ServerCallContext context)
    {
        var http = context.GetHttpContext();
        if (http is null || !http.Request.IsHttps)
            return;

        if (string.IsNullOrEmpty(_token) || !HeaderMatches(context.RequestHeaders, _token))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing or invalid bearer token."));
        }
    }

    /// <summary>True when <paramref name="headers"/> carry a matching Bearer token.</summary>
    public static bool HeaderMatches(Metadata headers, string expectedToken)
    {
        ArgumentNullException.ThrowIfNull(headers);
        if (string.IsNullOrEmpty(expectedToken))
            return false;

        foreach (var entry in headers)
        {
            if (!string.Equals(entry.Key, MetadataKey, StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryParseBearer(entry.Value, out var presented) && FixedEquals(presented, expectedToken))
                return true;
        }

        return false;
    }

    internal static bool TryParseBearer(string? value, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        const string prefix = "Bearer ";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        token = value.Substring(prefix.Length).Trim();
        return token.Length > 0;
    }

    internal static bool FixedEquals(string a, string b)
    {
        var left = System.Text.Encoding.UTF8.GetBytes(a);
        var right = System.Text.Encoding.UTF8.GetBytes(b);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}

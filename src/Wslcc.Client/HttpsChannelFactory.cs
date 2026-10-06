using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;

namespace Wslcc.Client;

internal static class HttpsChannelFactory
{
    public static GrpcChannel Create(Uri address, WslccClientOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, cert, chain, errors)
                    => ValidateServerCertificate(cert, chain, errors, options.TlsCaPath),
            },
        };

        GrpcChannel channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpHandler = handler });
        return channel;
    }

    public static global::Wslcc.Grpc.Contracts.Wslcc.WslccClient CreateClient(GrpcChannel channel, WslccEndpoint endpoint, WslccClientOptions options)
    {
        if (!endpoint.IsNamedPipe && !string.IsNullOrEmpty(options.Token))
            return new global::Wslcc.Grpc.Contracts.Wslcc.WslccClient(channel.Intercept(new BearerCallInterceptor(options.Token)));

        return new global::Wslcc.Grpc.Contracts.Wslcc.WslccClient(channel);
    }

    internal static bool ValidateServerCertificate(
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors errors,
        string? extraCaPath)
    {
        if (errors == SslPolicyErrors.None)
            return true;

        if (string.IsNullOrWhiteSpace(extraCaPath) || certificate is null || !File.Exists(extraCaPath))
            return false;

        using var extra = X509CertificateLoader.LoadCertificateFromFile(extraCaPath);
        using var server = certificate as X509Certificate2 ?? new X509Certificate2(certificate);

        using var custom = new X509Chain();
        custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        custom.ChainPolicy.CustomTrustStore.Add(extra);
        custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return custom.Build(server);
    }
}

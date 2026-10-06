using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Wslccd;

/// <summary>
/// Validates and binds the optional remote HTTPS endpoint. Plain HTTP is refused; TLS and a bearer
/// token are required whenever the endpoint is enabled.
/// </summary>
public static class HttpListenSetup
{
    public const string TokenEnvironmentVariable = "WSLCC_HTTP_TOKEN";

    /// <summary>Resolves the bearer token from config, a file, or <see cref="TokenEnvironmentVariable"/>.</summary>
    public static string? ResolveToken(DaemonOptions.HttpOptions http)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (!string.IsNullOrWhiteSpace(http.Token))
            return http.Token;

        if (!string.IsNullOrWhiteSpace(http.TokenFile))
        {
            if (!File.Exists(http.TokenFile))
                throw new InvalidOperationException($"Wslcc:Http:TokenFile not found: {http.TokenFile}");

            return File.ReadAllText(http.TokenFile).TrimEnd('\r', '\n');
        }

        var fromEnv = Environment.GetEnvironmentVariable(TokenEnvironmentVariable);
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv;
    }

    /// <summary>
    /// Throws when HTTP is enabled without <c>https://</c>, a readable PEM pair, and a non-empty token.
    /// No-op when the endpoint is disabled.
    /// </summary>
    public static void Validate(DaemonOptions.HttpOptions http)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (!http.Enabled)
            return;

        if (!Uri.TryCreate(http.Url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Wslcc:Http:Url is not a valid absolute URI.");

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Wslcc:Http:Url must use https://. Plain HTTP is refused. Generate a certificate with 'wslcc daemon cert'.");
        }

        if (string.IsNullOrWhiteSpace(http.CertificatePath) || !File.Exists(http.CertificatePath))
        {
            throw new InvalidOperationException(
                "Wslcc:Http:CertificatePath is required and must exist when HTTP is enabled. Run 'wslcc daemon cert'.");
        }

        if (string.IsNullOrWhiteSpace(http.CertificateKeyPath) || !File.Exists(http.CertificateKeyPath))
        {
            throw new InvalidOperationException(
                "Wslcc:Http:CertificateKeyPath is required and must exist when HTTP is enabled. Run 'wslcc daemon cert'.");
        }

        var token = ResolveToken(http);
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException(
                "HTTP requires a bearer token: set Wslcc:Http:Token, Wslcc:Http:TokenFile, or the WSLCC_HTTP_TOKEN environment variable.");
        }
    }

    /// <summary>Where Kestrel should listen for the HTTPS URL (host is honored).</summary>
    public static HttpBindTarget ResolveBind(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var port = uri.IsDefaultPort ? 443 : uri.Port;
        var host = uri.IdnHost;

        if (host is "0.0.0.0" or "::" or "+" or "*")
            return new HttpBindTarget(HttpBindKind.Any, port, Address: null);

        if (host is "localhost" or "127.0.0.1" or "::1")
            return new HttpBindTarget(HttpBindKind.Loopback, port, Address: null);

        if (IPAddress.TryParse(host, out var address))
            return new HttpBindTarget(HttpBindKind.Address, port, address);

        // DNS name: listen on all interfaces; the certificate SAN must match the name clients use.
        return new HttpBindTarget(HttpBindKind.Any, port, Address: null);
    }

    /// <summary>Binds Kestrel for HTTPS when enabled. Call <see cref="Validate"/> first.</summary>
    public static void Apply(KestrelServerOptions kestrel, DaemonOptions.HttpOptions http)
    {
        ArgumentNullException.ThrowIfNull(kestrel);
        ArgumentNullException.ThrowIfNull(http);

        if (!http.Enabled)
            return;

        var uri = new Uri(http.Url, UriKind.Absolute);
        var target = ResolveBind(uri);
        var certificate = LoadCertificate(http.CertificatePath!, http.CertificateKeyPath!);

        void Configure(ListenOptions listen)
        {
            listen.Protocols = HttpProtocols.Http2;
            listen.UseHttps(certificate);
        }

        switch (target.Kind)
        {
            case HttpBindKind.Loopback:
                kestrel.ListenLocalhost(target.Port, Configure);
                break;
            case HttpBindKind.Address:
                kestrel.Listen(target.Address!, target.Port, Configure);
                break;
            default:
                kestrel.ListenAnyIP(target.Port, Configure);
                break;
        }
    }

    public static X509Certificate2 LoadCertificate(string certificatePath, string keyPath)
    {
        var loaded = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);

        // Windows Kestrel needs an ephemeral cert with a persistable private key.
        var exported = loaded.Export(X509ContentType.Pkcs12);
        loaded.Dispose();
        return X509CertificateLoader.LoadPkcs12(exported, password: null);
    }
}

public enum HttpBindKind
{
    Any,
    Loopback,
    Address,
}

public readonly record struct HttpBindTarget(HttpBindKind Kind, int Port, IPAddress? Address);

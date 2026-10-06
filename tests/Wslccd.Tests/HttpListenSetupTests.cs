using System.Net;
using Grpc.Core;
using Wslccd;

namespace Wslccd.Tests;

public sealed class HttpListenSetupTests
{
    [Fact]
    public void Validate_is_noop_when_http_is_disabled()
    {
        HttpListenSetup.Validate(new DaemonOptions.HttpOptions { Enabled = false, Url = "http://0.0.0.0:1" });
    }

    [Fact]
    public void Validate_rejects_plain_http()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            HttpListenSetup.Validate(new DaemonOptions.HttpOptions
            {
                Enabled = true,
                Url = "http://0.0.0.0:5211",
            }));

        Assert.Contains("https://", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_rejects_missing_certificate()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            HttpListenSetup.Validate(new DaemonOptions.HttpOptions
            {
                Enabled = true,
                Url = "https://127.0.0.1:5211",
                CertificatePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pem"),
                CertificateKeyPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".key"),
                Token = "secret",
            }));

        Assert.Contains("CertificatePath", ex.Message);
    }

    [Fact]
    public void Validate_rejects_missing_token()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var cert = Path.Combine(dir.FullName, "c.pem");
            var key = Path.Combine(dir.FullName, "k.pem");
            File.WriteAllText(cert, "placeholder");
            File.WriteAllText(key, "placeholder");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                HttpListenSetup.Validate(new DaemonOptions.HttpOptions
                {
                    Enabled = true,
                    Url = "https://127.0.0.1:5211",
                    CertificatePath = cert,
                    CertificateKeyPath = key,
                }));

            Assert.Contains("token", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ResolveBind_honors_loopback_any_and_address()
    {
        var loopback = HttpListenSetup.ResolveBind(new Uri("https://127.0.0.1:5211"));
        Assert.Equal(HttpBindKind.Loopback, loopback.Kind);
        Assert.Equal(5211, loopback.Port);

        var any = HttpListenSetup.ResolveBind(new Uri("https://0.0.0.0:5211"));
        Assert.Equal(HttpBindKind.Any, any.Kind);

        var address = HttpListenSetup.ResolveBind(new Uri("https://192.0.2.10:8443"));
        Assert.Equal(HttpBindKind.Address, address.Kind);
        Assert.Equal(IPAddress.Parse("192.0.2.10"), address.Address);
        Assert.Equal(8443, address.Port);
    }

    [Fact]
    public void HeaderMatches_accepts_bearer_token()
    {
        var headers = new Metadata { { "authorization", "Bearer s3cret" } };
        Assert.True(HttpsBearerInterceptor.HeaderMatches(headers, "s3cret"));
        Assert.False(HttpsBearerInterceptor.HeaderMatches(headers, "other"));
        Assert.False(HttpsBearerInterceptor.HeaderMatches(new Metadata(), "s3cret"));
    }
}

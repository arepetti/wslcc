using Wslcc.Cli;
using Wslcc.Client;

namespace Wslcc.Cli.Tests;

public sealed class DaemonCertificateWriterTests
{
    [Fact]
    public void Write_creates_pem_pair_and_force_is_required_to_overwrite()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var first = DaemonCertificateWriter.Write(new DaemonCertificateRequest
            {
                OutputDirectory = dir.FullName,
                Hostnames = new[] { "localhost", "127.0.0.1" },
                Days = 30,
            });

            Assert.True(File.Exists(first.CertificatePath));
            Assert.True(File.Exists(first.KeyPath));
            Assert.Contains("BEGIN CERTIFICATE", File.ReadAllText(first.CertificatePath));
            Assert.Contains("BEGIN PRIVATE KEY", File.ReadAllText(first.KeyPath));
            Assert.False(string.IsNullOrWhiteSpace(first.Sha256Fingerprint));

            var ex = Assert.Throws<InvalidOperationException>(() =>
                DaemonCertificateWriter.Write(new DaemonCertificateRequest
                {
                    OutputDirectory = dir.FullName,
                    Force = false,
                }));
            Assert.Contains("--force", ex.Message);

            var second = DaemonCertificateWriter.Write(new DaemonCertificateRequest
            {
                OutputDirectory = dir.FullName,
                Force = true,
            });
            Assert.Equal(first.CertificatePath, second.CertificatePath);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void PatchAppsettings_sets_certificate_paths_without_enabling_http()
    {
        const string json = """
            {
              "Wslcc": {
                "Http": {
                  "Enabled": false,
                  "Url": "https://0.0.0.0:5211"
                }
              }
            }
            """;

        var patched = DaemonCertificateWriter.PatchAppsettings(json, @"C:\certs\server.pem", @"C:\certs\server.key");
        var http = System.Text.Json.Nodes.JsonNode.Parse(patched)!["Wslcc"]!["Http"]!;

        Assert.Equal(@"C:\certs\server.pem", (string?)http["CertificatePath"]);
        Assert.Equal(@"C:\certs\server.key", (string?)http["CertificateKeyPath"]);
        Assert.Equal(false, (bool?)http["Enabled"]);
    }

    [Fact]
    public void WslccClient_rejects_plain_http()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new WslccClient("http://example:5211", new WslccClientOptions { Token = "x" }));

        Assert.Contains("Plain HTTP", ex.Message);
    }

    [Fact]
    public void WslccClient_requires_token_for_https()
    {
        var ex = Assert.Throws<ArgumentException>(() => new WslccClient("https://example:5211"));

        Assert.Contains("bearer token", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

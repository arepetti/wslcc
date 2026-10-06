using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Wslcc.Cli;

/// <summary>Creates a self-signed PEM certificate pair for the daemon HTTPS endpoint.</summary>
public static class DaemonCertificateWriter
{
    public static string DefaultDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wslcc", "certs");

    public static DaemonCertificateResult Write(DaemonCertificateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var hostnames = request.Hostnames is { Count: > 0 }
            ? request.Hostnames
            : new[] { "localhost", "127.0.0.1" };

        var directory = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? DefaultDirectory()
            : request.OutputDirectory;

        Directory.CreateDirectory(directory);

        var certPath = Path.Combine(directory, "server.pem");
        var keyPath = Path.Combine(directory, "server.key");

        if ((File.Exists(certPath) || File.Exists(keyPath)) && !request.Force)
        {
            throw new InvalidOperationException(
                $"A certificate already exists in '{directory}'. Pass --force to overwrite.");
        }

        using var rsa = RSA.Create(2048);
        var subjectName = hostnames[0];
        var requestCert = new CertificateRequest(
            $"CN={subjectName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        requestCert.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        requestCert.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        requestCert.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        requestCert.CertificateExtensions.Add(BuildSubjectAlternativeName(hostnames));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = notBefore.AddDays(request.Days <= 0 ? 365 : request.Days);
        using var cert = requestCert.CreateSelfSigned(notBefore, notAfter);

        File.WriteAllText(certPath, cert.ExportCertificatePem() + Environment.NewLine);
        File.WriteAllText(keyPath, rsa.ExportPkcs8PrivateKeyPem() + Environment.NewLine);
        TryRestrictKeyAccess(keyPath);

        var fingerprint = Convert.ToHexString(SHA256.HashData(cert.RawData));
        return new DaemonCertificateResult(certPath, keyPath, fingerprint, hostnames);
    }

    public static string PatchAppsettings(string json, string certificatePath, string keyPath)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json)
            as System.Text.Json.Nodes.JsonObject
            ?? new System.Text.Json.Nodes.JsonObject();

        var wslcc = root["Wslcc"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
        root["Wslcc"] = wslcc;

        var http = wslcc["Http"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
        wslcc["Http"] = http;

        http["CertificatePath"] = certificatePath;
        http["CertificateKeyPath"] = keyPath;

        return root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    private static X509Extension BuildSubjectAlternativeName(IReadOnlyList<string> hostnames)
    {
        var builder = new SubjectAlternativeNameBuilder();
        foreach (var name in hostnames)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (IPAddress.TryParse(name, out var ip))
                builder.AddIpAddress(ip);
            else
                builder.AddDnsName(name);
        }

        return builder.Build();
    }

    private static void TryRestrictKeyAccess(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception)
        {
        }
    }
}

public sealed class DaemonCertificateRequest
{
    public IReadOnlyList<string> Hostnames { get; init; } = Array.Empty<string>();

    public string? OutputDirectory { get; init; }

    public int Days { get; init; } = 365;

    public bool Force { get; init; }
}

public sealed record DaemonCertificateResult(
    string CertificatePath,
    string KeyPath,
    string Sha256Fingerprint,
    IReadOnlyList<string> Hostnames);

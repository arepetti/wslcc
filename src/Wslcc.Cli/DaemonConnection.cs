using Wslcc.Client;

namespace Wslcc.Cli;

/// <summary>Builds a <see cref="WslccClient"/> from CLI flags and <c>WSLCC_TOKEN</c> / <c>WSLCC_TLS_CA</c>.</summary>
internal static class DaemonConnection
{
    public const string TokenEnvironmentVariable = "WSLCC_TOKEN";

    public const string TlsCaEnvironmentVariable = "WSLCC_TLS_CA";

    public static WslccClient Create(string? host, string? token = null, string? tlsCa = null)
    {
        return new WslccClient(host, new WslccClientOptions
        {
            Token = FirstNonEmpty(token, Environment.GetEnvironmentVariable(TokenEnvironmentVariable)),
            TlsCaPath = FirstNonEmpty(tlsCa, Environment.GetEnvironmentVariable(TlsCaEnvironmentVariable)),
        });
    }

    private static string? FirstNonEmpty(string? preferred, string? fallback)
        => !string.IsNullOrWhiteSpace(preferred) ? preferred : (string.IsNullOrWhiteSpace(fallback) ? null : fallback);
}

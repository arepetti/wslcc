namespace Wslcc.Grpc.Server;

/// <summary>Server-side options supplied by the host (daemon).</summary>
public sealed class WslccServerOptions
{
    /// <summary>Version string reported by the daemon for <c>GetVersion</c>.</summary>
    public string DaemonVersion { get; set; } = "0.0.0";

    /// <summary>The daemon's default provider, reported on <c>Ping</c>.</summary>
    public string DefaultProvider { get; set; } = string.Empty;
}

using Microsoft.Extensions.Hosting;
using Wslcc.Grpc.Server;

namespace Wslccd;

/// <summary>
/// Bridges the gRPC <c>Shutdown</c> RPC to the host lifetime so <c>wslcc daemon stop</c> can stop
/// a per-user daemon process gracefully.
/// </summary>
public sealed class DaemonLifetime : IDaemonLifetime
{
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<DaemonLifetime> _logger;

    public DaemonLifetime(IHostApplicationLifetime appLifetime, ILogger<DaemonLifetime> logger)
    {
        _appLifetime = appLifetime;
        _logger = logger;
    }

    public void RequestShutdown()
    {
        _logger.LogInformation("Shutdown requested via gRPC; stopping daemon.");
        _appLifetime.StopApplication();
    }
}

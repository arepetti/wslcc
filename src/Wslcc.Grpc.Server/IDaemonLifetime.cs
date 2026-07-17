namespace Wslcc.Grpc.Server;

/// <summary>
/// Abstraction the daemon implements so the gRPC service can request a graceful shutdown
/// without taking a dependency on the hosting model.
/// </summary>
public interface IDaemonLifetime
{
    void RequestShutdown();
}

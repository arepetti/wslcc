using Wslcc.Grpc.Contracts;

namespace Wslcc.Cli;

internal static class ServiceResults
{
    public static int FailedCount(IEnumerable<ServiceResult> results)
        => results.Count(r => string.Equals(r.Status, "failed", StringComparison.OrdinalIgnoreCase));
}

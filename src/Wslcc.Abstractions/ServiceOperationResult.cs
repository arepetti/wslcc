namespace Wslcc.Abstractions;

/// <summary>Outcome of an up/down operation for a single service.</summary>
/// <param name="Service">Compose service the outcome belongs to.</param>
/// <param name="Status">
/// Terminal status of the operation (e.g. <c>started</c>, <c>running</c>, <c>stopped</c>,
/// <c>pulled</c>, <c>skipped</c>, <c>failed</c>).
/// </param>
/// <param name="ContainerId">Id of the container involved, when one was created or acted on.</param>
/// <param name="Error">Failure detail; set when <see cref="Failed"/> is <c>true</c>.</param>
/// <exception cref="ArgumentException"><paramref name="Service"/> or <paramref name="Status"/> is null, empty or whitespace.</exception>
public sealed record ServiceOperationResult(
    string Service,
    string Status,
    string? ContainerId = null,
    string? Error = null)
{
    /// <summary>Compose service (or synthetic resource label) the outcome belongs to.</summary>
    public string Service { get; init; } = RequireService(Service);

    /// <summary>Terminal status of the operation.</summary>
    public string Status { get; init; } = RequireStatus(Status);

    /// <summary>Whether the operation failed for this service.</summary>
    public bool Failed => string.Equals(Status, "failed", StringComparison.OrdinalIgnoreCase);

    private static string RequireService(string service)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        return service;
    }

    private static string RequireStatus(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        return status;
    }
}

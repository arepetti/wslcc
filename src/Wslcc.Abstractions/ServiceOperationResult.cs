namespace Wslcc.Abstractions;

/// <summary>Outcome of an up/down operation for a single service.</summary>
public sealed record ServiceOperationResult(
    string Service,
    string Status,
    string? ContainerId = null,
    string? Error = null)
{
    public bool Failed => string.Equals(Status, "failed", StringComparison.OrdinalIgnoreCase);
}

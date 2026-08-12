namespace Wslcc.Abstractions;

/// <summary>
/// Per-service progress reported while a compose operation runs. Used to stream status to gRPC
/// clients (CLI/GUI) instead of waiting for a single unary response.
/// </summary>
/// <param name="Service">Compose service name (or a synthetic label such as <c>network …</c>).</param>
/// <param name="Phase">What the engine is doing (<c>pulling</c>, <c>building</c>, <c>creating</c>, …).</param>
/// <param name="Status">
/// <c>in_progress</c> while work is underway; otherwise a terminal status matching
/// <see cref="ServiceOperationResult.Status"/> (<c>started</c>, <c>pulled</c>, <c>failed</c>, …).
/// </param>
/// <param name="Message">Optional detail (typically an error message when <paramref name="Status"/> is failed).</param>
/// <param name="ContainerId">Container id when known.</param>
public sealed record ServiceProgressUpdate(
    string Service,
    string Phase,
    string Status,
    string? Message = null,
    string? ContainerId = null)
{
    public const string InProgress = "in_progress";

    public bool IsInProgress => string.Equals(Status, InProgress, StringComparison.OrdinalIgnoreCase);
}

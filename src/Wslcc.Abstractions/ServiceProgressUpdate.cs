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
/// <exception cref="ArgumentNullException"><paramref name="Service"/> is <c>null</c>.</exception>
/// <exception cref="ArgumentException"><paramref name="Phase"/> or <paramref name="Status"/> is null, empty or whitespace.</exception>
public sealed record ServiceProgressUpdate(
    string Service,
    string Phase,
    string Status,
    string? Message = null,
    string? ContainerId = null)
{
    /// <summary>The <see cref="Status"/> value that marks an update as non-terminal.</summary>
    public const string InProgress = "in_progress";

    /// <summary>
    /// Compose service name, or an empty string for a project-level update (such as a <c>ps</c> listing)
    /// that belongs to no single service.
    /// </summary>
    public string Service { get; init; } = RequireService(Service);

    /// <summary>What the engine is doing (<c>pulling</c>, <c>building</c>, <c>creating</c>, …).</summary>
    public string Phase { get; init; } = RequirePhase(Phase);

    /// <summary><c>in_progress</c> while work is underway, otherwise a terminal status.</summary>
    public string Status { get; init; } = RequireStatus(Status);

    /// <summary>Whether this update reports work still underway rather than a final outcome.</summary>
    public bool IsInProgress => string.Equals(Status, InProgress, StringComparison.OrdinalIgnoreCase);

    private static string RequireService(string service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return service;
    }

    private static string RequirePhase(string phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        return phase;
    }

    private static string RequireStatus(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        return status;
    }
}

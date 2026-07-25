namespace Wslcc.Abstractions;

/// <summary>
/// A snapshot of a container's runtime state, used to evaluate <c>depends_on</c> conditions.
/// </summary>
/// <param name="Status">The lifecycle status (e.g. <c>running</c>, <c>exited</c>, <c>created</c>).</param>
/// <param name="Health">The healthcheck status, or <see cref="HealthStatus.None"/> when none is configured.</param>
/// <param name="ExitCode">The exit code once the container has stopped; meaningful only when exited.</param>
public sealed record ContainerRuntimeState(string Status, HealthStatus Health, int? ExitCode)
{
    /// <summary>Whether the container has stopped (exited or dead).</summary>
    public bool HasExited =>
        string.Equals(Status, "exited", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Status, "dead", StringComparison.OrdinalIgnoreCase);
}

namespace Wslcc.Abstractions.Compose;

/// <summary>
/// The <c>depends_on</c> condition that must hold for a dependency before its dependent is started.
/// </summary>
public enum DependencyCondition
{
    /// <summary>The dependency's container has been started (Compose's default; start-order only).</summary>
    ServiceStarted = 0,

    /// <summary>The dependency's container reports a healthy healthcheck.</summary>
    ServiceHealthy = 1,

    /// <summary>The dependency's container has run to completion with exit code 0.</summary>
    ServiceCompletedSuccessfully = 2,
}

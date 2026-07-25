namespace Wslcc.Abstractions;

/// <summary>A container's healthcheck status as reported by the provider.</summary>
public enum HealthStatus
{
    /// <summary>The container has no healthcheck configured.</summary>
    None = 0,

    /// <summary>A healthcheck is configured but has not yet passed (still within its start period / retries).</summary>
    Starting = 1,

    /// <summary>The healthcheck is passing.</summary>
    Healthy = 2,

    /// <summary>The healthcheck has failed enough times to be considered unhealthy.</summary>
    Unhealthy = 3,
}

namespace Wslcc.Abstractions;

/// <summary>
/// A provider-agnostic healthcheck to apply when creating a container. <see cref="Command"/> is a
/// single shell command (Compose's <c>CMD-SHELL</c> semantics); durations are kept as their string
/// form (e.g. <c>"30s"</c>). When <see cref="Disabled"/> is set, any image-baked healthcheck is turned
/// off and the other fields are ignored.
/// </summary>
public sealed class ContainerHealthCheck
{
    /// <summary>Turns off any healthcheck baked into the image; the remaining fields are then ignored.</summary>
    public bool Disabled { get; set; }

    /// <summary>The shell command to run; <c>null</c> keeps the image's own healthcheck command.</summary>
    public string? Command { get; set; }

    /// <summary>Delay between checks, as a duration string (e.g. <c>"30s"</c>).</summary>
    public string? Interval { get; set; }

    /// <summary>How long a single check may run before it counts as failed, as a duration string.</summary>
    public string? Timeout { get; set; }

    /// <summary>Consecutive failures required before the container is reported unhealthy.</summary>
    public int? Retries { get; set; }

    /// <summary>Grace period after start during which failures do not count, as a duration string.</summary>
    public string? StartPeriod { get; set; }
}

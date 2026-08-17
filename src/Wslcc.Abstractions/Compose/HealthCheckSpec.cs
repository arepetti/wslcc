namespace Wslcc.Abstractions.Compose;

/// <summary>
/// A service's <c>healthcheck:</c> section. <see cref="Test"/> holds the normalized command tokens as
/// written in Compose (e.g. <c>["CMD-SHELL", "curl -f http://localhost || exit 1"]</c> or a single
/// element for the string short form). <see cref="Disabled"/> is set for <c>disable: true</c> or a
/// <c>["NONE"]</c> test. Durations are kept as their Compose strings (e.g. <c>"30s"</c>).
/// </summary>
public sealed class HealthCheckSpec
{
    /// <summary>Set by <c>disable: true</c> or a <c>["NONE"]</c> test; turns the image's healthcheck off.</summary>
    public bool Disabled { get; set; }

    /// <summary>The <c>test:</c> tokens as written (e.g. <c>CMD-SHELL</c> followed by the command).</summary>
    public IList<string> Test { get; set; } = new List<string>();

    /// <summary>Compose <c>interval:</c> — delay between checks (e.g. <c>"30s"</c>).</summary>
    public string? Interval { get; set; }

    /// <summary>Compose <c>timeout:</c> — how long one check may run before it counts as failed.</summary>
    public string? Timeout { get; set; }

    /// <summary>Compose <c>retries:</c> — consecutive failures before the container is unhealthy.</summary>
    public int? Retries { get; set; }

    /// <summary>Compose <c>start_period:</c> — grace period after start during which failures do not count.</summary>
    public string? StartPeriod { get; set; }
}

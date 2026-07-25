namespace Wslcc.Abstractions.Compose;

/// <summary>
/// A service's <c>healthcheck:</c> section. <see cref="Test"/> holds the normalized command tokens as
/// written in Compose (e.g. <c>["CMD-SHELL", "curl -f http://localhost || exit 1"]</c> or a single
/// element for the string short form). <see cref="Disabled"/> is set for <c>disable: true</c> or a
/// <c>["NONE"]</c> test. Durations are kept as their Compose strings (e.g. <c>"30s"</c>).
/// </summary>
public sealed class HealthCheckSpec
{
    public bool Disabled { get; set; }

    public IList<string> Test { get; set; } = new List<string>();

    public string? Interval { get; set; }

    public string? Timeout { get; set; }

    public int? Retries { get; set; }

    public string? StartPeriod { get; set; }
}

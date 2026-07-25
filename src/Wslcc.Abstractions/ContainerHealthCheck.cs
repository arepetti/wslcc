namespace Wslcc.Abstractions;

/// <summary>
/// A provider-agnostic healthcheck to apply when creating a container. <see cref="Command"/> is a
/// single shell command (Compose's <c>CMD-SHELL</c> semantics); durations are kept as their string
/// form (e.g. <c>"30s"</c>). When <see cref="Disabled"/> is set, any image-baked healthcheck is turned
/// off and the other fields are ignored.
/// </summary>
public sealed class ContainerHealthCheck
{
    public bool Disabled { get; set; }

    public string? Command { get; set; }

    public string? Interval { get; set; }

    public string? Timeout { get; set; }

    public int? Retries { get; set; }

    public string? StartPeriod { get; set; }
}

namespace Wslcc.Abstractions.Compose;

/// <summary>
/// A top-level <c>secrets:</c> entry. Exactly one of <see cref="File"/> or <see cref="Environment"/>
/// is set; <c>external</c> secrets are rejected at parse time (no Swarm secret store).
/// </summary>
public sealed class SecretSpec
{
    /// <summary>Key as written under <c>secrets:</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Compose <c>file:</c> — host path whose contents are the secret (unresolved until run).</summary>
    public string? File { get; set; }

    /// <summary>Compose <c>environment:</c> — name of the host environment variable that holds the value.</summary>
    public string? Environment { get; set; }
}

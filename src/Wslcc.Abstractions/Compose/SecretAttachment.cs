namespace Wslcc.Abstractions.Compose;

/// <summary>
/// One service <c>secrets:</c> entry — a grant of a top-level secret into the container as a file.
/// </summary>
public sealed class SecretAttachment
{
    /// <summary>Top-level secret name (<c>source</c>, or the short-form string).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Path inside the container. Short form defaults to <c>/run/secrets/&lt;source&gt;</c>.
    /// </summary>
    public string Target { get; set; } = string.Empty;
}

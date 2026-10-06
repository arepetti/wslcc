namespace Wslcc.Client;

/// <summary>Optional HTTPS credentials for a remote <see cref="WslccClient"/>.</summary>
public sealed class WslccClientOptions
{
    /// <summary>Bearer token sent as <c>Authorization: Bearer</c> on HTTPS calls.</summary>
    public string? Token { get; init; }

    /// <summary>
    /// PEM file of the extra CA (or self-signed server cert) to trust. When unset, the system
    /// trust store is used.
    /// </summary>
    public string? TlsCaPath { get; init; }
}

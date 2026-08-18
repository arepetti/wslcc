namespace Wslcc.Abstractions.Compose;

/// <summary>Compose long-form <c>volumes[].type</c> values that WSLCC applies.</summary>
public enum MountType
{
    /// <summary>Named or anonymous volume (<c>type: volume</c> or short named/anonymous syntax).</summary>
    Volume,

    /// <summary>Host path bind mount (<c>type: bind</c> or short bind syntax).</summary>
    Bind,

    /// <summary>In-memory tmpfs (<c>type: tmpfs</c> or service <c>tmpfs:</c>).</summary>
    Tmpfs,
}

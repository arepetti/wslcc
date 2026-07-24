namespace Wslcc.Abstractions;

/// <summary>
/// Controls whether <c>up</c> (re)builds services that declare a <c>build:</c> section.
/// </summary>
public enum BuildPolicy
{
    /// <summary>
    /// Build a <c>build:</c> service only when its target image is not present locally. This is the
    /// default and mirrors <c>docker compose up</c>.
    /// </summary>
    Auto = 0,

    /// <summary>Always (re)build a <c>build:</c> service before starting it (<c>up --build</c>).</summary>
    Always = 1,

    /// <summary>
    /// Never build; a <c>build:</c> service whose image is missing fails instead of being built
    /// (<c>up --no-build</c>).
    /// </summary>
    Never = 2,
}

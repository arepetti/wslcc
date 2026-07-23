namespace Wslcc.Compose;

/// <summary>
/// Raised when a Compose document cannot be loaded/resolved on the client: an unresolved required
/// variable (<c>${VAR:?msg}</c>), a missing <c>extends</c> target, an <c>extends</c> cycle, malformed
/// YAML, and so on. Carries a user-facing message; the CLI renders it and exits non-zero.
/// </summary>
public sealed class ComposeLoadException : Exception
{
    public ComposeLoadException(string message)
        : base(message)
    {
    }

    public ComposeLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

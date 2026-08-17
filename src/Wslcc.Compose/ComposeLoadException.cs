namespace Wslcc.Compose;

/// <summary>
/// Raised when a Compose document cannot be loaded/resolved on the client: an unresolved required
/// variable (<c>${VAR:?msg}</c>), a missing <c>extends</c> target, an <c>extends</c> cycle, malformed
/// YAML, and so on. Carries a user-facing message; the CLI renders it and exits non-zero.
/// </summary>
public sealed class ComposeLoadException : Exception
{
    /// <summary>Creates the exception with a user-facing description of the problem.</summary>
    /// <param name="message">Message describing what could not be loaded and why.</param>
    public ComposeLoadException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception wrapping the underlying failure.</summary>
    /// <param name="message">Message describing what could not be loaded and why.</param>
    /// <param name="innerException">The underlying error (e.g. the YAML parser's exception).</param>
    public ComposeLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

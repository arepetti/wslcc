namespace Wslcc.Abstractions;

/// <summary>Thrown by providers when a container operation fails.</summary>
public sealed class ProviderException : Exception
{
    /// <summary>Creates the exception with a user-facing description of the failed operation.</summary>
    /// <param name="message">Message describing what failed and why.</param>
    public ProviderException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception wrapping the underlying failure.</summary>
    /// <param name="message">Message describing what failed and why.</param>
    /// <param name="innerException">The underlying error.</param>
    public ProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

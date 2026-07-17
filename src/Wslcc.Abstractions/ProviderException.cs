namespace Wslcc.Abstractions;

/// <summary>Thrown by providers when a container operation fails.</summary>
public sealed class ProviderException : Exception
{
    public ProviderException(string message)
        : base(message)
    {
    }

    public ProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

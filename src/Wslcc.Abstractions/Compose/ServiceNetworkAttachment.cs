namespace Wslcc.Abstractions.Compose;

/// <summary>Provider-relevant options from a service's long-form network attachment.</summary>
public sealed class ServiceNetworkAttachment
{
    public IList<string> Aliases { get; set; } = new List<string>();

    public string? IPv4Address { get; set; }
}

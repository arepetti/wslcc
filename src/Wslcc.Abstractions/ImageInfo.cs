namespace Wslcc.Abstractions;

/// <summary>
/// An image as reported by a provider.
/// </summary>
public sealed record ImageInfo(
    string Id,
    string Repository,
    string Tag,
    long SizeBytes);

namespace Wslcc.Abstractions;

/// <summary>
/// An image as reported by a provider.
/// </summary>
/// <param name="Id">Provider-assigned image id.</param>
/// <param name="Repository">Repository part of the image reference (e.g. <c>nginx</c>).</param>
/// <param name="Tag">Tag part of the image reference (e.g. <c>alpine</c>).</param>
/// <param name="SizeBytes">On-disk size of the image, in bytes.</param>
public sealed record ImageInfo(
    string Id,
    string Repository,
    string Tag,
    long SizeBytes);

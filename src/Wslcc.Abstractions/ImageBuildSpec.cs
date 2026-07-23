namespace Wslcc.Abstractions;

/// <summary>
/// A provider-agnostic description of an image to build from a Dockerfile. The compose engine builds
/// one of these per service with a <c>build:</c> section; providers translate it into their CLI/API
/// calls (e.g. <c>docker build</c>).
/// </summary>
public sealed class ImageBuildSpec
{
    /// <summary>Absolute (or CLI-resolvable) path to the build context.</summary>
    public string Context { get; set; } = string.Empty;

    public string? Dockerfile { get; set; }

    public string? Target { get; set; }

    public IDictionary<string, string?> Args { get; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>Tag to apply to the built image, e.g. "<project>-<service>".</summary>
    public string Tag { get; set; } = string.Empty;
}

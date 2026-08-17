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

    /// <summary>Dockerfile to build from; <c>null</c> uses the context's default <c>Dockerfile</c>.</summary>
    public string? Dockerfile { get; set; }

    /// <summary>Multi-stage build stage to stop at; <c>null</c> builds the final stage.</summary>
    public string? Target { get; set; }

    /// <summary>
    /// Build arguments. A <c>null</c> value means "pass the name through" so the value is inherited
    /// from the build environment.
    /// </summary>
    public IDictionary<string, string?> Args { get; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>Tag to apply to the built image, e.g. "&lt;project&gt;-&lt;service&gt;".</summary>
    public string Tag { get; set; } = string.Empty;
}

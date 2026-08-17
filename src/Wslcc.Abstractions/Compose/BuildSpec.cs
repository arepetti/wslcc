namespace Wslcc.Abstractions.Compose;

/// <summary>
/// Build configuration for a service. Supports both the shorthand string form
/// (<c>build: .</c>) and the long map form (<c>build: { context, dockerfile, args }</c>).
/// </summary>
public sealed class BuildSpec
{
    /// <summary>Build context path, as written in the file (relative paths resolve against the project directory).</summary>
    public string? Context { get; set; }

    /// <summary>Dockerfile to build from; <c>null</c> uses the context's default <c>Dockerfile</c>.</summary>
    public string? Dockerfile { get; set; }

    /// <summary>Multi-stage build stage to stop at; <c>null</c> builds the final stage.</summary>
    public string? Target { get; set; }

    /// <summary>
    /// <c>args:</c> entries. A <c>null</c> value is a name with no value, inherited from the build
    /// environment.
    /// </summary>
    public IDictionary<string, string?> Args { get; set; } = new Dictionary<string, string?>(StringComparer.Ordinal);
}

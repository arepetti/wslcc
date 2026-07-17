namespace Wslcc.Abstractions.Compose;

/// <summary>
/// Build configuration for a service. Supports both the shorthand string form
/// (<c>build: .</c>) and the long map form (<c>build: { context, dockerfile, args }</c>).
/// </summary>
public sealed class BuildSpec
{
    public string? Context { get; set; }

    public string? Dockerfile { get; set; }

    public string? Target { get; set; }

    public IDictionary<string, string?> Args { get; set; } = new Dictionary<string, string?>(StringComparer.Ordinal);
}

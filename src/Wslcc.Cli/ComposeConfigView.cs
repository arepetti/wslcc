using Wslcc.Compose;

namespace Wslcc.Cli;

/// <summary>
/// Derives the list-mode outputs of <c>wslcc compose config</c> (<c>--services</c> / <c>--volumes</c> /
/// <c>--images</c>) from an already-resolved Compose document. Names are returned sorted (ordinal) so the
/// output is deterministic regardless of declaration order.
/// </summary>
public static class ComposeConfigView
{
    public static IReadOnlyList<string> ServiceNames(string resolvedYaml)
        => Parse(resolvedYaml).Services.Keys
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyList<string> VolumeNames(string resolvedYaml)
        => Parse(resolvedYaml).Volumes.Keys
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyList<string> ImageNames(string resolvedYaml)
        => Parse(resolvedYaml).Services.Values
            .Select(service => service.Image)
            .Where(image => !string.IsNullOrWhiteSpace(image))
            .Select(image => image!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(image => image, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Resolves the effective project name (explicit <c>-p</c> &gt; the document's <c>name:</c> &gt; the
    /// directory-derived default), sanitized the same way the daemon does.
    /// </summary>
    public static string ResolveProjectName(string resolvedYaml, string? explicitName, string defaultName)
        => ProjectNames.Resolve(explicitName, Parse(resolvedYaml), defaultName);

    /// <summary>
    /// Renders the resolved document with the effective <paramref name="projectName"/> injected as a
    /// leading <c>name:</c> (mirroring <c>docker compose config</c>), as YAML or, when
    /// <paramref name="asJson"/> is set, JSON.
    /// </summary>
    public static string Render(string resolvedYaml, string projectName, bool asJson)
    {
        var root = YamlGraph.AsMap(YamlGraph.Deserialize(resolvedYaml)) ?? new Dictionary<string, object?>(StringComparer.Ordinal);

        var withName = new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = projectName };
        foreach (var kvp in root)
        {
            if (!string.Equals(kvp.Key, "name", StringComparison.Ordinal))
            {
                withName[kvp.Key] = kvp.Value;
            }
        }

        return asJson ? YamlGraph.SerializeJson(withName) : YamlGraph.Serialize(withName);
    }

    private static Wslcc.Abstractions.Compose.ComposeFile Parse(string resolvedYaml)
        => new ComposeFileParser().Parse(resolvedYaml);
}

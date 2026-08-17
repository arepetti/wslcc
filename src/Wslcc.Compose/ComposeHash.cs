using System.Security.Cryptography;
using System.Text;

namespace Wslcc.Compose;

/// <summary>
/// Computes a stable per-service configuration hash for a resolved Compose document (backs
/// <c>wslcc compose config --hash</c>). The hash is the SHA-256 of the service's configuration in a
/// canonical form (mapping keys sorted recursively) so it is independent of key order. It is a
/// wslcc-specific digest for change detection, not compatible with Docker Compose's own hash.
/// </summary>
public static class ComposeHash
{
    /// <summary>Returns <c>service name → hex SHA-256</c> for every service in the resolved document.</summary>
    /// <param name="resolvedYaml">The fully-resolved, single-document Compose YAML.</param>
    /// <returns>One lowercase hex digest per service; empty when the document declares no services.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resolvedYaml"/> is <c>null</c>.</exception>
    /// <exception cref="ComposeLoadException"><paramref name="resolvedYaml"/> is not valid YAML.</exception>
    public static IReadOnlyDictionary<string, string> ComputeServiceHashes(string resolvedYaml)
    {
        ArgumentNullException.ThrowIfNull(resolvedYaml);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        var services = TryGetServicesMap(resolvedYaml);
        if (services is null)
            return result;

        foreach (var kvp in services)
            result[kvp.Key] = Hash(kvp.Value);

        return result;
    }

    private static Dictionary<string, object?>? TryGetServicesMap(string resolvedYaml)
    {
        if (YamlGraph.AsMap(YamlGraph.Deserialize(resolvedYaml)) is not { } root)
            return null;

        return YamlGraph.AsMap(root.TryGetValue("services", out var services) ? services : null);
    }

    private static string Hash(object? node)
    {
        var canonical = YamlGraph.SerializeJson(Canonicalize(node));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Rebuilds the graph with every mapping's keys ordered, so serialization is deterministic.</summary>
    private static object? Canonicalize(object? node)
    {
        switch (node)
        {
            case Dictionary<string, object?> map:
                var ordered = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var key in map.Keys.OrderBy(k => k, StringComparer.Ordinal))
                    ordered[key] = Canonicalize(map[key]);

                return ordered;
            case List<object?> list:
                return list.Select(Canonicalize).ToList();
            default:
                return node;
        }
    }
}

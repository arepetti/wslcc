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
    public static IReadOnlyDictionary<string, string> ComputeServiceHashes(string resolvedYaml)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (YamlGraph.AsMap(YamlGraph.Deserialize(resolvedYaml)) is not { } root
            || YamlGraph.AsMap(root.TryGetValue("services", out var s) ? s : null) is not { } services)
        {
            return result;
        }

        foreach (var kvp in services)
        {
            result[kvp.Key] = Hash(kvp.Value);
        }

        return result;
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
                {
                    ordered[key] = Canonicalize(map[key]);
                }

                return ordered;
            case List<object?> list:
                return list.Select(Canonicalize).ToList();
            default:
                return node;
        }
    }
}

using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Wslcc.Compose;

/// <summary>
/// Helpers for working with a Compose document as a generic YAML node graph
/// (<see cref="Dictionary{TKey,TValue}"/> of <c>string</c> to <c>object?</c> for mappings,
/// <see cref="List{T}"/> of <c>object?</c> for sequences, and scalars). Operating on the graph — rather
/// than the strongly-typed model — lets the client merge, interpolate and resolve <c>extends</c> while
/// preserving keys WSLCC does not model yet, then re-serialize a single fully-resolved document.
/// </summary>
public static class YamlGraph
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();
    private static readonly ISerializer Serializer = new SerializerBuilder().Build();

    /// <summary>Deserializes YAML into a normalized graph (mappings keyed by ordinal strings).</summary>
    public static object? Deserialize(string yaml)
    {
        try
        {
            return Normalize(Deserializer.Deserialize<object?>(yaml));
        }
        catch (YamlException ex)
        {
            throw new ComposeLoadException($"Invalid YAML: {ex.Message}", ex);
        }
    }

    public static string Serialize(object? graph) => Serializer.Serialize(graph ?? new Dictionary<string, object?>());

    public static Dictionary<string, object?>? AsMap(object? node) => node as Dictionary<string, object?>;

    public static List<object?>? AsList(object? node) => node as List<object?>;

    /// <summary>
    /// Deep-merges <paramref name="overrideNode"/> onto <paramref name="baseNode"/>: mappings are merged
    /// recursively; scalars and sequences from the override replace the base (a <c>null</c> override
    /// keeps the base). Inputs are not mutated.
    /// </summary>
    public static object? DeepMerge(object? baseNode, object? overrideNode)
    {
        if (overrideNode is null)
        {
            return baseNode;
        }

        if (AsMap(baseNode) is { } baseMap && AsMap(overrideNode) is { } overrideMap)
        {
            var result = new Dictionary<string, object?>(baseMap, StringComparer.Ordinal);
            foreach (var kvp in overrideMap)
            {
                result[kvp.Key] = result.TryGetValue(kvp.Key, out var existing)
                    ? DeepMerge(existing, kvp.Value)
                    : kvp.Value;
            }

            return result;
        }

        return overrideNode;
    }

    /// <summary>Recursively interpolates every scalar string in the graph (mapping keys are left as-is).</summary>
    public static object? Interpolate(object? node, VariableInterpolator interpolator)
    {
        switch (node)
        {
            case Dictionary<string, object?> map:
                var newMap = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var kvp in map)
                {
                    newMap[kvp.Key] = Interpolate(kvp.Value, interpolator);
                }

                return newMap;
            case List<object?> list:
                return list.Select(item => Interpolate(item, interpolator)).ToList();
            case string s:
                return interpolator.Interpolate(s);
            default:
                return node;
        }
    }

    private static object? Normalize(object? node)
    {
        switch (node)
        {
            case null:
                return null;
            case string s:
                return s;
            case IDictionary<object, object?> rawMap:
                var map = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var kvp in rawMap)
                {
                    map[Convert.ToString(kvp.Key) ?? string.Empty] = Normalize(kvp.Value);
                }

                return map;
            case System.Collections.IEnumerable enumerable:
                var list = new List<object?>();
                foreach (var item in enumerable)
                {
                    list.Add(Normalize(item));
                }

                return list;
            default:
                return node;
        }
    }
}

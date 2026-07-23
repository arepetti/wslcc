namespace Wslcc.Compose;

/// <summary>
/// Compose-aware merge used both for multi-file overrides and for <c>extends</c>. It follows the
/// Compose specification's per-attribute rules rather than a blanket "override replaces":
/// <list type="bullet">
/// <item>Mapping attributes (and <c>environment</c>/<c>labels</c> written as a <c>KEY=VALUE</c> list) are merged by key.</item>
/// <item>Most sequence attributes are appended (with exact-duplicate entries removed).</item>
/// <item><c>command</c>/<c>entrypoint</c> are replaced wholesale (a command line is a single value).</item>
/// <item>Scalars are replaced.</item>
/// </list>
/// </summary>
public static class ComposeMerge
{
    // environment/labels/etc. may be a map or a "KEY=VALUE" list; both merge by key.
    private static readonly HashSet<string> MergeByKeyAttributes = new(StringComparer.Ordinal)
    {
        "environment", "labels", "annotations", "sysctls",
    };

    // A command line is a single value even though it is a sequence: the override replaces it.
    private static readonly HashSet<string> ReplaceAttributes = new(StringComparer.Ordinal)
    {
        "command", "entrypoint",
    };

    // May be a list (names) or a map (name -> condition/aliases); merge accordingly.
    private static readonly HashSet<string> DualFormAttributes = new(StringComparer.Ordinal)
    {
        "depends_on", "networks",
    };

    /// <summary>Merges two whole Compose documents (<paramref name="overrideDoc"/> wins).</summary>
    public static object? Merge(object? baseDoc, object? overrideDoc)
    {
        if (YamlGraph.AsMap(baseDoc) is not { } baseMap)
        {
            return overrideDoc ?? baseDoc;
        }

        if (YamlGraph.AsMap(overrideDoc) is not { } overrideMap)
        {
            return baseDoc;
        }

        var result = new Dictionary<string, object?>(baseMap, StringComparer.Ordinal);
        foreach (var kvp in overrideMap)
        {
            var hasExisting = result.TryGetValue(kvp.Key, out var existing);
            result[kvp.Key] = kvp.Key switch
            {
                "services" => MergeNamedMap(existing, kvp.Value, MergeService),
                "networks" or "volumes" or "configs" or "secrets" => MergeNamedMap(existing, kvp.Value, YamlGraph.DeepMerge),
                _ => hasExisting ? YamlGraph.DeepMerge(existing, kvp.Value) : kvp.Value,
            };
        }

        return result;
    }

    /// <summary>Merges two service definitions using the per-attribute rules (used by <c>extends</c> too).</summary>
    public static object? MergeService(object? baseService, object? overrideService)
    {
        if (YamlGraph.AsMap(baseService) is not { } baseMap)
        {
            return overrideService ?? baseService;
        }

        if (YamlGraph.AsMap(overrideService) is not { } overrideMap)
        {
            return baseService;
        }

        var result = new Dictionary<string, object?>(baseMap, StringComparer.Ordinal);
        foreach (var kvp in overrideMap)
        {
            result[kvp.Key] = result.TryGetValue(kvp.Key, out var existing)
                ? MergeAttribute(kvp.Key, existing, kvp.Value)
                : kvp.Value;
        }

        return result;
    }

    private static object? MergeAttribute(string key, object? baseValue, object? overrideValue)
    {
        if (MergeByKeyAttributes.Contains(key))
        {
            return MergeByKey(baseValue, overrideValue);
        }

        if (ReplaceAttributes.Contains(key))
        {
            return overrideValue;
        }

        if (DualFormAttributes.Contains(key))
        {
            return YamlGraph.AsMap(baseValue) is not null || YamlGraph.AsMap(overrideValue) is not null
                ? MergeByKey(baseValue, overrideValue)
                : AppendDedup(baseValue, overrideValue);
        }

        if (YamlGraph.AsList(baseValue) is not null && YamlGraph.AsList(overrideValue) is not null)
        {
            return AppendDedup(baseValue, overrideValue);
        }

        if (YamlGraph.AsMap(baseValue) is not null && YamlGraph.AsMap(overrideValue) is not null)
        {
            return YamlGraph.DeepMerge(baseValue, overrideValue);
        }

        return overrideValue;
    }

    private static Dictionary<string, object?> MergeNamedMap(
        object? baseNode, object? overrideNode, Func<object?, object?, object?> mergeEntry)
    {
        var result = YamlGraph.AsMap(baseNode) is { } baseMap
            ? new Dictionary<string, object?>(baseMap, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);

        if (YamlGraph.AsMap(overrideNode) is { } overrideMap)
        {
            foreach (var kvp in overrideMap)
            {
                result[kvp.Key] = result.TryGetValue(kvp.Key, out var existing)
                    ? mergeEntry(existing, kvp.Value)
                    : kvp.Value;
            }
        }

        return result;
    }

    /// <summary>Merges two mapping values by key; each side may be a map or a <c>KEY=VALUE</c> list.</summary>
    private static Dictionary<string, object?> MergeByKey(object? baseValue, object? overrideValue)
    {
        var result = ToKeyValueMap(baseValue);
        foreach (var kvp in ToKeyValueMap(overrideValue))
        {
            result[kvp.Key] = kvp.Value;
        }

        return result;
    }

    private static Dictionary<string, object?> ToKeyValueMap(object? value)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (YamlGraph.AsMap(value) is { } map)
        {
            foreach (var kvp in map)
            {
                result[kvp.Key] = kvp.Value;
            }

            return result;
        }

        if (YamlGraph.AsList(value) is { } list)
        {
            foreach (var item in list)
            {
                var text = Convert.ToString(item) ?? string.Empty;
                var eq = text.IndexOf('=');
                if (eq < 0)
                {
                    result[text] = null;
                }
                else
                {
                    result[text.Substring(0, eq)] = text.Substring(eq + 1);
                }
            }
        }

        return result;
    }

    /// <summary>Concatenates two sequences, dropping entries whose serialized form already appeared.</summary>
    private static List<object?> AppendDedup(object? baseValue, object? overrideValue)
    {
        var result = new List<object?>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in ToList(baseValue).Concat(ToList(overrideValue)))
        {
            var canonical = item is string s ? s : YamlGraph.Serialize(item);
            if (seen.Add(canonical))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static List<object?> ToList(object? value)
        => YamlGraph.AsList(value) is { } list ? list : value is null ? new List<object?>() : new List<object?> { value };
}

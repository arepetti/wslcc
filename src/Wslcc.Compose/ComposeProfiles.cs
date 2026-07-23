namespace Wslcc.Compose;

/// <summary>
/// Applies Compose profile activation. A service with no <c>profiles:</c> is always enabled; a service
/// that lists profiles is enabled only when at least one of them is active. Disabled services are
/// removed, the (now-resolved) <c>profiles:</c> key is dropped from survivors, and <c>depends_on</c>
/// references to removed services are pruned so ordering does not break.
/// </summary>
public static class ComposeProfiles
{
    public static object? Apply(object? graph, ISet<string> activeProfiles)
    {
        var root = YamlGraph.AsMap(graph);
        if (root is null || YamlGraph.AsMap(GetValue(root, "services")) is not { } services)
        {
            return graph;
        }

        var kept = new Dictionary<string, object?>(StringComparer.Ordinal);
        var removed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var kvp in services)
        {
            var service = YamlGraph.AsMap(kvp.Value);
            var profiles = GetProfiles(service);
            var enabled = profiles.Count == 0 || profiles.Any(activeProfiles.Contains);
            if (!enabled)
            {
                removed.Add(kvp.Key);
                continue;
            }

            if (service is not null && service.ContainsKey("profiles"))
            {
                var copy = new Dictionary<string, object?>(service, StringComparer.Ordinal);
                copy.Remove("profiles");
                kept[kvp.Key] = copy;
            }
            else
            {
                kept[kvp.Key] = kvp.Value;
            }
        }

        if (removed.Count > 0)
        {
            foreach (var name in kept.Keys.ToList())
            {
                if (YamlGraph.AsMap(kept[name]) is not { } service || !service.TryGetValue("depends_on", out var dependsOn))
                {
                    continue;
                }

                var pruned = PruneDependsOn(dependsOn, removed);
                var copy = new Dictionary<string, object?>(service, StringComparer.Ordinal);
                if (pruned is null)
                {
                    copy.Remove("depends_on");
                }
                else
                {
                    copy["depends_on"] = pruned;
                }

                kept[name] = copy;
            }
        }

        return new Dictionary<string, object?>(root, StringComparer.Ordinal)
        {
            ["services"] = kept,
        };
    }

    private static List<string> GetProfiles(Dictionary<string, object?>? service)
    {
        var result = new List<string>();
        if (service is not null && YamlGraph.AsList(GetValue(service, "profiles")) is { } list)
        {
            foreach (var item in list)
            {
                if (item is not null)
                {
                    result.Add(Convert.ToString(item) ?? string.Empty);
                }
            }
        }

        return result;
    }

    private static object? PruneDependsOn(object? dependsOn, HashSet<string> removed)
    {
        switch (dependsOn)
        {
            case List<object?> list:
                var keptItems = list.Where(i => !removed.Contains(Convert.ToString(i) ?? string.Empty)).ToList();
                return keptItems.Count > 0 ? keptItems : null;
            case Dictionary<string, object?> map:
                var keptMap = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var kvp in map)
                {
                    if (!removed.Contains(kvp.Key))
                    {
                        keptMap[kvp.Key] = kvp.Value;
                    }
                }

                return keptMap.Count > 0 ? keptMap : null;
            default:
                return dependsOn;
        }
    }

    private static object? GetValue(Dictionary<string, object?> map, string key)
        => map.TryGetValue(key, out var value) ? value : null;
}

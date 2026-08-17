namespace Wslcc.Compose;

/// <summary>
/// Resolves <c>extends</c> in a single Compose file's services. A service may extend another service
/// in the same file (<c>extends: base</c> or <c>extends: { service: base }</c>) or in another file
/// (<c>extends: { file: other.yml, service: base }</c>); relative <c>file</c> paths resolve against the
/// directory of the file that declares the <c>extends</c>. The base is resolved first (chains are
/// supported) and the child is merged over it with <see cref="ComposeMerge.MergeService"/>. Cycles are
/// rejected, as is extending a service that declares a non-extendable (service-referencing) attribute.
/// </summary>
// Compose file — extends: a service inherits another service, in this file or another one.
public static class ComposeExtends
{
    // Attributes that reference other services/containers and therefore cannot be inherited via extends.
    private static readonly string[] NonExtendableKeys = { "depends_on", "volumes_from", "links" };

    // Attributes that cannot be inherited when they reference a specific service/container.
    private static readonly string[] ReferenceKeys = { "network_mode", "ipc", "pid", "uts" };

    /// <summary>
    /// Returns a copy of <paramref name="filePath"/>'s graph with every service's <c>extends</c> resolved.
    /// <paramref name="loadInterpolated"/> maps an absolute file path to that file's already-interpolated
    /// graph (used for cross-file <c>extends</c>).
    /// </summary>
    /// <param name="filePath">Absolute path of the file whose services are resolved.</param>
    /// <param name="loadInterpolated">Reads and interpolates the document at a given absolute path.</param>
    /// <returns>
    /// The document with <c>extends</c> resolved away, or the input unchanged when it declares no
    /// <c>services:</c>.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="loadInterpolated"/> is <c>null</c>.</exception>
    /// <exception cref="ComposeLoadException">
    /// The <c>extends</c> is malformed, its target is missing, the chain is cyclic, or the base service
    /// declares an attribute that cannot be inherited.
    /// </exception>
    public static object? ResolveFile(string filePath, Func<string, object?> loadInterpolated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(loadInterpolated);

        var graph = loadInterpolated(filePath);
        var root = YamlGraph.AsMap(graph);
        if (root is null)
            return graph;

        if (YamlGraph.AsMap(GetValue(root, "services")) is not { } services)
            return graph;

        var resolvedServices = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var name in services.Keys)
            resolvedServices[name] = ResolveService(filePath, name, loadInterpolated, new HashSet<(string, string)>());

        var newRoot = new Dictionary<string, object?>(root, StringComparer.Ordinal)
        {
            ["services"] = resolvedServices,
        };
        return newRoot;
    }

    private static object? ResolveService(
        string filePath,
        string serviceName,
        Func<string, object?> loadInterpolated,
        HashSet<(string, string)> visiting)
    {
        var key = (filePath, serviceName);
        if (!visiting.Add(key))
            throw new ComposeLoadException($"'extends' cycle detected at service '{serviceName}' in '{filePath}'.");

        try
        {
            var raw = RequireService(filePath, serviceName, loadInterpolated);
            if (YamlGraph.AsMap(raw) is not { } serviceMap)
                return raw;

            if (GetValue(serviceMap, "extends") is not { } extendsNode)
                return serviceMap;

            var (baseService, baseFileRelative) = ParseExtends(extendsNode, serviceName, filePath);
            var baseFilePath = baseFileRelative is null
                ? filePath
                : Path.GetFullPath(Path.Combine(DirectoryOf(filePath), baseFileRelative));

            var resolvedBase = ResolveService(baseFilePath, baseService, loadInterpolated, visiting);
            EnsureExtendable(resolvedBase, baseService, baseFilePath);

            var child = new Dictionary<string, object?>(serviceMap, StringComparer.Ordinal);
            child.Remove("extends");

            return ComposeMerge.MergeService(resolvedBase, child);
        }
        finally
        {
            visiting.Remove(key);
        }
    }

    /// <summary>Reads one service's raw node, rejecting a missing file section or a missing service.</summary>
    private static object? RequireService(string filePath, string serviceName, Func<string, object?> loadInterpolated)
    {
        var services = YamlGraph.AsMap(GetValue(YamlGraph.AsMap(loadInterpolated(filePath)), "services"));
        if (services is null)
            throw MissingExtendsTarget(serviceName, filePath);

        if (!services.TryGetValue(serviceName, out var raw))
            throw MissingExtendsTarget(serviceName, filePath);

        return raw;
    }

    private static ComposeLoadException MissingExtendsTarget(string serviceName, string filePath)
        => new($"'extends' target service '{serviceName}' was not found in '{filePath}'.");

    /// <summary>Rejects extending a base service that declares an attribute referencing another service/container.</summary>
    private static void EnsureExtendable(object? resolvedBase, string serviceName, string filePath)
    {
        if (YamlGraph.AsMap(resolvedBase) is not { } map)
            return;

        foreach (var key in NonExtendableKeys)
        {
            if (map.ContainsKey(key))
            {
                throw new ComposeLoadException(
                    $"Service '{serviceName}' in '{filePath}' can't be extended because it declares '{key}'.");
            }
        }

        foreach (var key in ReferenceKeys)
        {
            if (GetValue(map, key) is not string value)
                continue;

            if (!ReferencesAnotherContainer(value))
                continue;

            throw new ComposeLoadException(
                $"Service '{serviceName}' in '{filePath}' can't be extended because '{key}: {value}' references another container.");
        }
    }

    /// <summary>Whether a <c>network_mode</c>-style value points at another service or container.</summary>
    private static bool ReferencesAnotherContainer(string value)
        => value.StartsWith("service:", StringComparison.Ordinal)
            || value.StartsWith("container:", StringComparison.Ordinal);

    private static (string Service, string? File) ParseExtends(object? extendsNode, string serviceName, string filePath)
    {
        switch (extendsNode)
        {
            case string s when s.Length > 0:
                return (s, null);
            case Dictionary<string, object?> map:
                if (GetValue(map, "service") is not string { Length: > 0 } service)
                    throw new ComposeLoadException($"'extends' on service '{serviceName}' in '{filePath}' must specify a 'service'.");

                var file = GetValue(map, "file") as string;
                return (service, string.IsNullOrEmpty(file) ? null : file);
            default:
                throw new ComposeLoadException($"'extends' on service '{serviceName}' in '{filePath}' is malformed.");
        }
    }

    private static object? GetValue(Dictionary<string, object?>? map, string key)
        => map is not null && map.TryGetValue(key, out var value) ? value : null;

    private static string DirectoryOf(string filePath)
        => Path.GetDirectoryName(filePath) is { Length: > 0 } dir ? dir : ".";
}

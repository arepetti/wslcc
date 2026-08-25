namespace Wslcc.Compose;

/// <summary>
/// Resolves top-level <c>include:</c> in a Compose document. Each entry is a nested project: its own
/// <c>project_directory</c> and interpolation env, relative host paths rewritten to absolute, then its
/// resources are copied into the including file. A name that already exists in the including file is an
/// error (unlike <c>-f</c> merge). Nested includes are allowed; cycles and remote URLs are rejected.
/// </summary>
public static class ComposeInclude
{
    private static readonly string[] ImportSections = { "services", "networks", "volumes", "configs", "secrets" };

    /// <summary>
    /// Expands <c>include:</c> in <paramref name="graph"/> (already interpolated in the including file's
    /// env) and returns a copy with included resources merged in and the <c>include</c> key removed.
    /// </summary>
    public static object? Resolve(
        object? graph,
        string includingFilePath,
        IReadOnlyDictionary<string, string> processEnvironment,
        bool interpolate,
        List<string> warnings,
        HashSet<string>? visiting = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(includingFilePath);
        ArgumentNullException.ThrowIfNull(processEnvironment);
        ArgumentNullException.ThrowIfNull(warnings);

        var root = YamlGraph.AsMap(graph);
        if (root is null || !root.ContainsKey("include"))
            return graph;

        visiting ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var includingFull = Path.GetFullPath(includingFilePath);
        visiting.Add(includingFull);

        var result = CloneMap(root);
        var includeNode = result["include"];
        result.Remove("include");

        foreach (var entry in ParseEntries(includeNode, includingFull))
        {
            var included = LoadEntry(entry, includingFull, processEnvironment, interpolate, warnings, visiting);
            Import(result, included, entry.DisplayPath);
        }

        return result;
    }

    private static object? LoadEntry(
        IncludeEntry entry,
        string includingFilePath,
        IReadOnlyDictionary<string, string> processEnvironment,
        bool interpolate,
        List<string> warnings,
        HashSet<string> visiting)
    {
        object? merged = null;
        foreach (var path in entry.Paths)
        {
            var loaded = LoadIncludedFile(path, entry.ProjectDirectory, entry.EnvFiles, processEnvironment, interpolate, warnings, visiting);
            merged = merged is null ? loaded : ComposeMerge.Merge(merged, loaded);
        }

        if (merged is null)
            throw new ComposeLoadException($"'include' in '{includingFilePath}' has no path.");

        RewriteHostPaths(merged, entry.ProjectDirectory);
        return merged;
    }

    private static object? LoadIncludedFile(
        string path,
        string projectDirectory,
        IReadOnlyList<string> envFiles,
        IReadOnlyDictionary<string, string> processEnvironment,
        bool interpolate,
        List<string> warnings,
        HashSet<string> visiting)
    {
        var full = Path.GetFullPath(path);
        if (!visiting.Add(full))
            throw new ComposeLoadException($"'include' cycle detected at '{full}'.");

        try
        {
            if (!File.Exists(full))
                throw new ComposeLoadException($"Included Compose file not found: {full}");

            var parsed = YamlGraph.Deserialize(File.ReadAllText(full));
            object? graph = parsed;
            if (interpolate)
            {
                var pool = BuildIncludedPool(projectDirectory, envFiles, processEnvironment, warnings);
                var interpolator = new VariableInterpolator(
                    name => pool.TryGetValue(name, out var value) ? value : null, warnings);
                graph = YamlGraph.Interpolate(parsed, interpolator);
            }

            graph = Resolve(graph, full, processEnvironment, interpolate, warnings, visiting);

            object? LoadInterpolated(string other)
            {
                var otherFull = Path.GetFullPath(other);
                if (!File.Exists(otherFull))
                    throw new ComposeLoadException($"Compose file not found: {otherFull}");

                var otherParsed = YamlGraph.Deserialize(File.ReadAllText(otherFull));
                if (!interpolate)
                    return otherParsed;

                var pool = BuildIncludedPool(projectDirectory, envFiles, processEnvironment, warnings);
                var interpolator = new VariableInterpolator(
                    name => pool.TryGetValue(name, out var value) ? value : null, warnings);
                return YamlGraph.Interpolate(otherParsed, interpolator);
            }

            return ComposeExtends.ResolveFile(full, graph, LoadInterpolated);
        }
        finally
        {
            visiting.Remove(full);
        }
    }

    private static Dictionary<string, string> BuildIncludedPool(
        string projectDirectory,
        IReadOnlyList<string> envFiles,
        IReadOnlyDictionary<string, string> processEnvironment,
        List<string> warnings)
    {
        string? Lookup(string name) => processEnvironment.TryGetValue(name, out var value) ? value : null;

        var pool = new Dictionary<string, string>(StringComparer.Ordinal);
        if (envFiles.Count == 0)
        {
            Overlay(pool, EnvFile.Load(Path.Combine(projectDirectory, ".env"), Lookup, warnings));
        }
        else
        {
            foreach (var envFile in envFiles)
            {
                if (!File.Exists(envFile))
                    throw new ComposeLoadException($"Included env file not found: {envFile}");

                Overlay(pool, EnvFile.Load(envFile, Lookup, warnings));
            }
        }

        Overlay(pool, processEnvironment);
        return pool;
    }

    private static void Overlay(Dictionary<string, string> pool, IEnumerable<KeyValuePair<string, string>> values)
    {
        foreach (var kvp in values)
            pool[kvp.Key] = kvp.Value;
    }

    private static void Import(Dictionary<string, object?> parent, object? included, string sourcePath)
    {
        if (YamlGraph.AsMap(included) is not { } includedRoot)
            return;

        foreach (var section in ImportSections)
        {
            if (YamlGraph.AsMap(GetValue(includedRoot, section)) is not { } incoming)
                continue;

            if (YamlGraph.AsMap(GetValue(parent, section)) is not { } existing)
            {
                parent[section] = Clone(incoming);
                continue;
            }

            var merged = CloneMap(existing);
            foreach (var kvp in incoming)
            {
                if (merged.ContainsKey(kvp.Key))
                {
                    throw new ComposeLoadException(
                        $"'include' from '{sourcePath}' redefines {section} '{kvp.Key}' that already exists in the including file.");
                }

                merged[kvp.Key] = Clone(kvp.Value);
            }

            parent[section] = merged;
        }
    }

    private static void RewriteHostPaths(object? document, string projectDirectory)
    {
        if (YamlGraph.AsMap(GetValue(YamlGraph.AsMap(document), "services")) is not { } services)
            return;

        foreach (var service in services.Values)
        {
            if (YamlGraph.AsMap(service) is not { } map)
                continue;

            RewriteVolumes(map, projectDirectory);
            RewriteEnvFile(map, projectDirectory);
            RewriteBuild(map, projectDirectory);
        }
    }

    private static void RewriteVolumes(Dictionary<string, object?> service, string projectDirectory)
    {
        if (!service.TryGetValue("volumes", out var volumes) || volumes is null)
            return;

        if (volumes is string s)
        {
            service["volumes"] = RewriteShortVolume(s, projectDirectory);
            return;
        }

        if (YamlGraph.AsList(volumes) is not { } list)
            return;

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is string shortForm)
                list[i] = RewriteShortVolume(shortForm, projectDirectory);
            else if (YamlGraph.AsMap(list[i]) is { } longForm)
                RewriteLongVolume(longForm, projectDirectory);
        }
    }

    private static string RewriteShortVolume(string spec, string projectDirectory)
    {
        if (!LooksLikeBindMount(spec))
            return spec;

        var targetIdx = spec.IndexOf(":/", StringComparison.Ordinal);
        if (targetIdx <= 0)
            return spec;

        var source = spec.Substring(0, targetIdx);
        if (!IsRelativeHostPath(source))
            return spec;

        return ResolvePath(projectDirectory, source) + spec.Substring(targetIdx);
    }

    private static void RewriteLongVolume(Dictionary<string, object?> volume, string projectDirectory)
    {
        var type = GetValue(volume, "type") as string;
        if (string.Equals(type, "volume", StringComparison.Ordinal)
            || string.Equals(type, "tmpfs", StringComparison.Ordinal)
            || string.Equals(type, "npipe", StringComparison.Ordinal)
            || string.Equals(type, "cluster", StringComparison.Ordinal)
            || string.Equals(type, "image", StringComparison.Ordinal))
        {
            return;
        }

        if (GetValue(volume, "source") is not string source || !IsRelativeHostPath(source))
            return;

        if (type is not null && !string.Equals(type, "bind", StringComparison.Ordinal) && !LooksLikeBindSource(source))
            return;

        volume["source"] = ResolvePath(projectDirectory, source);
    }

    private static void RewriteEnvFile(Dictionary<string, object?> service, string projectDirectory)
    {
        if (!service.TryGetValue("env_file", out var envFile) || envFile is null)
            return;

        if (envFile is string s)
        {
            service["env_file"] = RewriteRelativePath(s, projectDirectory);
            return;
        }

        if (YamlGraph.AsList(envFile) is not { } list)
            return;

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is string path)
                list[i] = RewriteRelativePath(path, projectDirectory);
            else if (YamlGraph.AsMap(list[i]) is { } map && GetValue(map, "path") is string p)
                map["path"] = RewriteRelativePath(p, projectDirectory);
        }
    }

    private static void RewriteBuild(Dictionary<string, object?> service, string projectDirectory)
    {
        if (!service.TryGetValue("build", out var build) || build is null)
            return;

        if (build is string context)
        {
            service["build"] = RewriteRelativePath(context, projectDirectory);
            return;
        }

        if (YamlGraph.AsMap(build) is not { } map)
            return;

        if (GetValue(map, "context") is string ctx)
            map["context"] = RewriteRelativePath(ctx, projectDirectory);

        if (GetValue(map, "dockerfile") is string dockerfile)
            map["dockerfile"] = RewriteRelativePath(dockerfile, projectDirectory);
    }

    private static string RewriteRelativePath(string path, string projectDirectory)
        => IsRelativeHostPath(path) ? ResolvePath(projectDirectory, path) : path;

    private static bool LooksLikeBindMount(string spec)
        => LooksLikeBindSource(spec);

    private static bool LooksLikeBindSource(string source)
    {
        if (string.IsNullOrEmpty(source))
            return false;

        if (source.StartsWith("./", StringComparison.Ordinal)
            || source.StartsWith("../", StringComparison.Ordinal)
            || source.StartsWith(".\\", StringComparison.Ordinal)
            || source.StartsWith("..\\", StringComparison.Ordinal)
            || source.StartsWith('/')
            || source.StartsWith('\\')
            || source.StartsWith('~'))
        {
            return true;
        }

        return source.Length >= 2 && char.IsLetter(source[0]) && source[1] == ':';
    }

    private static bool IsRelativeHostPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.StartsWith('~'))
            return false;

        return !Path.IsPathRooted(path);
    }

    private static string ResolvePath(string projectDirectory, string relative)
        => Path.GetFullPath(Path.Combine(projectDirectory, relative));

    private static IReadOnlyList<IncludeEntry> ParseEntries(object? includeNode, string includingFilePath)
    {
        var includingDir = DirectoryOf(includingFilePath);
        var entries = new List<IncludeEntry>();

        if (includeNode is string path)
        {
            entries.Add(ParseShort(path, includingDir));
            return entries;
        }

        if (YamlGraph.AsMap(includeNode) is { } single)
        {
            entries.Add(ParseLong(single, includingDir, includingFilePath));
            return entries;
        }

        if (YamlGraph.AsList(includeNode) is not { } list)
            throw new ComposeLoadException($"'include' in '{includingFilePath}' must be a path, a mapping, or a list.");

        foreach (var item in list)
        {
            if (item is string shortPath)
                entries.Add(ParseShort(shortPath, includingDir));
            else if (YamlGraph.AsMap(item) is { } map)
                entries.Add(ParseLong(map, includingDir, includingFilePath));
            else
                throw new ComposeLoadException($"'include' entry in '{includingFilePath}' is malformed.");
        }

        return entries;
    }

    private static IncludeEntry ParseShort(string path, string includingDir)
        => ParseLong(new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = path }, includingDir, path);

    private static IncludeEntry ParseLong(Dictionary<string, object?> map, string includingDir, string display)
    {
        var paths = new List<string>();
        if (GetValue(map, "path") is string one)
        {
            paths.Add(one);
        }
        else if (YamlGraph.AsList(GetValue(map, "path")) is { } list)
        {
            foreach (var item in list)
            {
                if (item is not string p || string.IsNullOrWhiteSpace(p))
                    throw new ComposeLoadException($"'include.path' in '{display}' must be a path or a list of paths.");

                paths.Add(p);
            }
        }
        else
        {
            throw new ComposeLoadException($"'include' entry in '{display}' must specify 'path'.");
        }

        if (paths.Count == 0)
            throw new ComposeLoadException($"'include' entry in '{display}' must specify 'path'.");

        for (var i = 0; i < paths.Count; i++)
        {
            RejectRemote(paths[i]);
            paths[i] = Path.GetFullPath(Path.Combine(includingDir, paths[i]));
        }

        var projectDirectory = GetValue(map, "project_directory") as string;
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            projectDirectory = DirectoryOf(paths[0]);
        }
        else
        {
            RejectRemote(projectDirectory);
            projectDirectory = Path.GetFullPath(Path.Combine(includingDir, projectDirectory));
        }

        var envFiles = new List<string>();
        var envNode = GetValue(map, "env_file");
        if (envNode is string envOne)
        {
            RejectRemote(envOne);
            envFiles.Add(Path.GetFullPath(Path.Combine(includingDir, envOne)));
        }
        else if (YamlGraph.AsList(envNode) is { } envList)
        {
            foreach (var item in envList)
            {
                if (item is not string envPath || string.IsNullOrWhiteSpace(envPath))
                    throw new ComposeLoadException($"'include.env_file' in '{display}' must be a path or a list of paths.");

                RejectRemote(envPath);
                envFiles.Add(Path.GetFullPath(Path.Combine(includingDir, envPath)));
            }
        }

        return new IncludeEntry(paths, projectDirectory, envFiles, paths[0]);
    }

    private static void RejectRemote(string path)
    {
        if (path.Contains("://", StringComparison.Ordinal)
            || path.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            throw new ComposeLoadException(
                $"'include' path '{path}' is not a local file. Git, OCI and HTTP includes are not supported.");
        }
    }

    private static object? GetValue(Dictionary<string, object?>? map, string key)
        => map is not null && map.TryGetValue(key, out var value) ? value : null;

    private static string DirectoryOf(string filePath)
        => Path.GetDirectoryName(filePath) is { Length: > 0 } dir ? dir : ".";

    private static Dictionary<string, object?> CloneMap(Dictionary<string, object?> map)
    {
        var clone = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kvp in map)
            clone[kvp.Key] = Clone(kvp.Value);

        return clone;
    }

    private static object? Clone(object? node) => node switch
    {
        Dictionary<string, object?> map => CloneMap(map),
        List<object?> list => list.Select(Clone).ToList(),
        _ => node,
    };

    private sealed record IncludeEntry(
        IReadOnlyList<string> Paths,
        string ProjectDirectory,
        IReadOnlyList<string> EnvFiles,
        string DisplayPath);
}

using System.Collections;

namespace Wslcc.Compose;

/// <summary>Inputs for <see cref="ComposeLoader.Load"/>.</summary>
public sealed class ComposeLoadOptions
{
    /// <summary>Compose files to merge, in override order (later files win). At least one is required.</summary>
    public required IReadOnlyList<string> Files { get; init; }

    /// <summary>Profiles activated on the command line (unioned with <c>COMPOSE_PROFILES</c> and the profiles of <see cref="TargetedServices"/>).</summary>
    public IReadOnlyList<string> Profiles { get; init; } = Array.Empty<string>();

    /// <summary>Services named explicitly on the command line; their profiles are auto-activated (docker-compose behavior).</summary>
    public IReadOnlyList<string> TargetedServices { get; init; } = Array.Empty<string>();

    /// <summary>Explicit <c>--env-file</c> path; when null, a <c>.env</c> in the project directory is used if present.</summary>
    public string? EnvFilePath { get; init; }

    /// <summary>Explicit <c>--project-directory</c> (absolute). When null, the first compose file's directory is used.</summary>
    public string? ProjectDirectory { get; init; }

    /// <summary>Current working directory; used as the default project directory when <see cref="ProjectDirectory"/> is null.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>Process environment used for interpolation (overrides <c>.env</c>). Defaults to the real environment; injectable for tests.</summary>
    public IReadOnlyDictionary<string, string>? ProcessEnvironment { get; init; }

    /// <summary>When <c>false</c>, <c>${VAR}</c> references are left verbatim (Compose's <c>--no-interpolate</c>). Files are still merged, <c>extends</c> resolved and profiles filtered.</summary>
    public bool Interpolate { get; init; } = true;
}

/// <summary>Result of resolving a Compose project on the client.</summary>
public sealed class ComposeLoadResult
{
    /// <summary>The fully-resolved, single-document Compose YAML to hand to the daemon.</summary>
    public required string ResolvedYaml { get; init; }

    /// <summary>Directory of the first compose file (the project directory).</summary>
    public required string ProjectDirectory { get; init; }

    /// <summary>Interpolation warnings (e.g. an unset variable defaulted to a blank string).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>All profile names declared across services (before profile filtering), sorted and de-duplicated.</summary>
    public IReadOnlyList<string> DeclaredProfiles { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Resolves a Compose project on the client into a single document: it merges multiple files, loads
/// <c>.env</c>, interpolates <c>${VAR}</c> references, resolves <c>extends</c>, and filters services by
/// profile — then re-serializes the result. Files and environment are read where the CLI runs, so the
/// daemon receives an already-resolved document (it may run elsewhere).
/// </summary>
public static class ComposeLoader
{
    public static ComposeLoadResult Load(ComposeLoadOptions options)
    {
        if (options.Files.Count == 0)
        {
            throw new ComposeLoadException("No compose files specified.");
        }

        var projectDirectory = options.ProjectDirectory
            ?? Path.GetDirectoryName(Path.GetFullPath(options.Files[0]))
            ?? options.WorkingDirectory;
        var envDirectory = options.ProjectDirectory ?? options.WorkingDirectory;

        var warnings = new List<string>();
        var pool = BuildVariablePool(options, envDirectory, warnings);
        var interpolator = new VariableInterpolator(
            name => pool.TryGetValue(name, out var value) ? value : null, warnings);

        var cache = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        object? LoadInterpolated(string path)
        {
            var full = Path.GetFullPath(path);
            if (cache.TryGetValue(full, out var cached))
            {
                return cached;
            }

            if (!File.Exists(full))
            {
                throw new ComposeLoadException($"Compose file not found: {full}");
            }

            var parsed = YamlGraph.Deserialize(File.ReadAllText(full));
            var graph = options.Interpolate ? YamlGraph.Interpolate(parsed, interpolator) : parsed;
            cache[full] = graph;
            return graph;
        }

        object? merged = null;
        foreach (var file in options.Files)
        {
            var resolved = ComposeExtends.ResolveFile(Path.GetFullPath(file), LoadInterpolated);
            merged = merged is null ? resolved : ComposeMerge.Merge(merged, resolved);
        }

        var declaredProfiles = CollectDeclaredProfiles(merged);

        var activeProfiles = BuildActiveProfiles(options, pool);
        AddTargetedServiceProfiles(merged, options.TargetedServices, activeProfiles);
        merged = ComposeProfiles.Apply(merged, activeProfiles);

        return new ComposeLoadResult
        {
            ResolvedYaml = YamlGraph.Serialize(merged),
            ProjectDirectory = projectDirectory,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToList(),
            DeclaredProfiles = declaredProfiles,
        };
    }

    /// <summary>Gathers every profile named under any <c>services.*.profiles</c> (before filtering).</summary>
    private static IReadOnlyList<string> CollectDeclaredProfiles(object? merged)
    {
        if (YamlGraph.AsMap(merged) is not { } root
            || YamlGraph.AsMap(root.TryGetValue("services", out var s) ? s : null) is not { } services)
        {
            return Array.Empty<string>();
        }

        var profiles = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var service in services.Values)
        {
            if (YamlGraph.AsMap(service) is { } serviceMap
                && YamlGraph.AsList(serviceMap.TryGetValue("profiles", out var p) ? p : null) is { } list)
            {
                foreach (var profile in list)
                {
                    if (Convert.ToString(profile) is { Length: > 0 } name)
                    {
                        profiles.Add(name);
                    }
                }
            }
        }

        return profiles.ToList();
    }

    private static Dictionary<string, string> BuildVariablePool(ComposeLoadOptions options, string envDirectory, List<string> warnings)
    {
        var processEnvironment = options.ProcessEnvironment is { } provided
            ? new Dictionary<string, string>(provided, StringComparer.Ordinal)
            : CaptureProcessEnvironment();

        // .env values are interpolated against variables set earlier in the file, then the process
        // environment (which also overrides .env for the final pool, matching docker-compose).
        string? EnvLookup(string name) => processEnvironment.TryGetValue(name, out var value) ? value : null;

        IReadOnlyDictionary<string, string> envValues;
        if (options.EnvFilePath is { } explicitEnv)
        {
            var full = Path.GetFullPath(explicitEnv);
            if (!File.Exists(full))
            {
                throw new ComposeLoadException($"--env-file not found: {full}");
            }

            envValues = EnvFile.Load(full, EnvLookup, warnings);
        }
        else
        {
            envValues = EnvFile.Load(Path.Combine(envDirectory, ".env"), EnvLookup, warnings);
        }

        var pool = new Dictionary<string, string>(StringComparer.Ordinal);
        Overlay(pool, envValues);
        Overlay(pool, processEnvironment);
        return pool;
    }

    private static HashSet<string> BuildActiveProfiles(ComposeLoadOptions options, IReadOnlyDictionary<string, string> pool)
    {
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in options.Profiles)
        {
            if (!string.IsNullOrWhiteSpace(profile))
            {
                active.Add(profile.Trim());
            }
        }

        if (pool.TryGetValue("COMPOSE_PROFILES", out var fromEnv) && !string.IsNullOrWhiteSpace(fromEnv))
        {
            foreach (var part in fromEnv.Split(','))
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    active.Add(part.Trim());
                }
            }
        }

        return active;
    }

    /// <summary>Adds the profiles of any explicitly-targeted service to the active set (targeting a service enables its profile).</summary>
    private static void AddTargetedServiceProfiles(object? merged, IReadOnlyList<string> targeted, HashSet<string> active)
    {
        if (targeted.Count == 0
            || YamlGraph.AsMap(merged) is not { } root
            || YamlGraph.AsMap(root.TryGetValue("services", out var s) ? s : null) is not { } services)
        {
            return;
        }

        foreach (var name in targeted)
        {
            if (services.TryGetValue(name, out var service)
                && YamlGraph.AsMap(service) is { } serviceMap
                && YamlGraph.AsList(serviceMap.TryGetValue("profiles", out var p) ? p : null) is { } profiles)
            {
                foreach (var profile in profiles)
                {
                    if (profile is not null)
                    {
                        active.Add(Convert.ToString(profile) ?? string.Empty);
                    }
                }
            }
        }
    }

    private static void Overlay(Dictionary<string, string> pool, IEnumerable<KeyValuePair<string, string>> values)
    {
        foreach (var kvp in values)
        {
            pool[kvp.Key] = kvp.Value;
        }
    }

    private static Dictionary<string, string> CaptureProcessEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = Convert.ToString(entry.Key);
            if (!string.IsNullOrEmpty(key))
            {
                result[key] = Convert.ToString(entry.Value) ?? string.Empty;
            }
        }

        return result;
    }
}

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

    /// <summary>When <c>false</c>, <c>${VAR}</c> references are left verbatim (Compose's <c>--no-interpolate</c>). Files are still merged, <c>include</c>/<c>extends</c> resolved and profiles filtered.</summary>
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
/// <c>.env</c>, interpolates <c>${VAR}</c> references, resolves <c>include</c> and <c>extends</c>, and
/// filters services by profile — then re-serializes the result. Files and environment are read where
/// the CLI runs, so the daemon receives an already-resolved document (it may run elsewhere).
/// </summary>
/// <example>
/// Resolving the project in the current directory and parsing the result:
/// <code>
/// var files = ComposeFileDiscovery.Discover(
///     specified: Array.Empty&lt;string&gt;(), searchDirectory: cwd, environment: environment);
///
/// var result = ComposeLoader.Load(new ComposeLoadOptions
/// {
///     Files = files,
///     WorkingDirectory = cwd,
///     Profiles = new[] { "debug" },
/// });
///
/// foreach (var warning in result.Warnings)
/// {
///     Console.Error.WriteLine(warning);
/// }
///
/// ComposeFile file = new ComposeFileParser().Parse(result.ResolvedYaml);
/// </code>
/// </example>
public static class ComposeLoader
{
    /// <summary>
    /// Resolves the project described by <paramref name="options"/> into a single Compose document.
    /// </summary>
    /// <param name="options">Files to merge, profiles to activate, and the environment to interpolate against.</param>
    /// <returns>The resolved YAML plus the project directory, interpolation warnings and declared profiles.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or its <see cref="ComposeLoadOptions.Files"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// <see cref="ComposeLoadOptions.WorkingDirectory"/> is null, empty or whitespace.
    /// </exception>
    /// <exception cref="ComposeLoadException">
    /// No files were given, a compose file (or an <c>extends</c>/<c>include</c> target) is missing, or an explicit
    /// <c>--env-file</c> does not exist.
    /// </exception>
    public static ComposeLoadResult Load(ComposeLoadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Files);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkingDirectory);

        if (options.Files.Count == 0)
            throw new ComposeLoadException("No compose files specified.");

        var warnings = new List<string>();
        var envDirectory = options.ProjectDirectory ?? options.WorkingDirectory;
        var processEnvironment = options.ProcessEnvironment is { } provided
            ? new Dictionary<string, string>(provided, StringComparer.Ordinal)
            : CaptureProcessEnvironment();
        var pool = BuildVariablePool(options, envDirectory, processEnvironment, warnings);

        var merged = MergeFiles(options, pool, processEnvironment, warnings);

        // Collected before filtering, so `config --profiles` can list profiles that are not active.
        var declaredProfiles = CollectDeclaredProfiles(merged);
        merged = FilterByProfiles(options, pool, merged);

        return new ComposeLoadResult
        {
            ResolvedYaml = YamlGraph.Serialize(merged),
            ProjectDirectory = ResolveProjectDirectory(options),
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToList(),
            DeclaredProfiles = declaredProfiles,
        };
    }

    /// <summary>
    /// The project directory: an explicit <c>--project-directory</c>, else the first compose file's
    /// directory, else the working directory.
    /// </summary>
    private static string ResolveProjectDirectory(ComposeLoadOptions options)
        => options.ProjectDirectory
            ?? Path.GetDirectoryName(Path.GetFullPath(options.Files[0]))
            ?? options.WorkingDirectory;

    /// <summary>
    /// Loads every compose file, resolves its <c>include</c> then <c>extends</c>, and merges the
    /// results in override order (later files win). Documents are cached by full path so an
    /// <c>extends</c> target referenced more than once is read and interpolated only once.
    /// </summary>
    private static object? MergeFiles(
        ComposeLoadOptions options,
        IReadOnlyDictionary<string, string> pool,
        IReadOnlyDictionary<string, string> processEnvironment,
        List<string> warnings)
    {
        var interpolator = new VariableInterpolator(
            name => pool.TryGetValue(name, out var value) ? value : null, warnings);
        var cache = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        object? LoadInterpolated(string path) => LoadDocument(path, options.Interpolate, interpolator, cache);

        object? merged = null;
        foreach (var file in options.Files)
        {
            var full = Path.GetFullPath(file);
            var interpolated = LoadInterpolated(full);
            var withIncludes = ComposeInclude.Resolve(
                interpolated, full, processEnvironment, options.Interpolate, warnings);
            var resolved = ComposeExtends.ResolveFile(full, withIncludes, LoadInterpolated);
            merged = merged is null ? resolved : ComposeMerge.Merge(merged, resolved);
        }

        return merged;
    }

    /// <summary>Reads one compose document from disk (interpolating it unless disabled), memoized by full path.</summary>
    private static object? LoadDocument(
        string path,
        bool interpolate,
        VariableInterpolator interpolator,
        Dictionary<string, object?> cache)
    {
        var full = Path.GetFullPath(path);
        if (cache.TryGetValue(full, out var cached))
            return cached;

        if (!File.Exists(full))
            throw new ComposeLoadException($"Compose file not found: {full}");

        var parsed = YamlGraph.Deserialize(File.ReadAllText(full));
        var graph = interpolate ? YamlGraph.Interpolate(parsed, interpolator) : parsed;
        cache[full] = graph;
        return graph;
    }

    /// <summary>
    /// Compose file — profiles: a service with profiles is kept only when one of them is active.
    /// The active set is the command line, <c>COMPOSE_PROFILES</c>, and the profiles of any service
    /// named explicitly on the command line.
    /// </summary>
    private static object? FilterByProfiles(
        ComposeLoadOptions options,
        IReadOnlyDictionary<string, string> pool,
        object? merged)
    {
        var active = BuildActiveProfiles(options, pool);
        AddTargetedServiceProfiles(merged, options.TargetedServices, active);
        return ComposeProfiles.Apply(merged, active);
    }

    /// <summary>Gathers every profile named under any <c>services.*.profiles</c> (before filtering).</summary>
    private static IReadOnlyList<string> CollectDeclaredProfiles(object? merged)
    {
        var services = TryGetServicesMap(merged);
        if (services is null)
            return Array.Empty<string>();

        var profiles = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var service in services.Values)
        {
            var list = TryGetProfilesList(service);
            if (list is null)
                continue;

            foreach (var profile in list)
            {
                if (Convert.ToString(profile) is { Length: > 0 } name)
                    profiles.Add(name);
            }
        }

        return profiles.ToList();
    }

    /// <summary>
    /// The variables <c>${VAR}</c> references resolve against: the <c>.env</c> file overlaid by the
    /// process environment (which wins, matching docker-compose).
    /// </summary>
    private static Dictionary<string, string> BuildVariablePool(
        ComposeLoadOptions options,
        string envDirectory,
        IReadOnlyDictionary<string, string> processEnvironment,
        List<string> warnings)
    {
        var envValues = LoadEnvValues(options, envDirectory, processEnvironment, warnings);

        var pool = new Dictionary<string, string>(StringComparer.Ordinal);
        Overlay(pool, envValues);
        Overlay(pool, processEnvironment);
        return pool;
    }

    /// <summary>
    /// Reads the explicit <c>--env-file</c>, or the project directory's <c>.env</c> when none was given
    /// (a missing default <c>.env</c> is not an error, an explicit one is). Its values are interpolated
    /// against variables set earlier in the file and the process environment.
    /// </summary>
    private static IReadOnlyDictionary<string, string> LoadEnvValues(
        ComposeLoadOptions options,
        string envDirectory,
        IReadOnlyDictionary<string, string> processEnvironment,
        List<string> warnings)
    {
        string? EnvLookup(string name) => processEnvironment.TryGetValue(name, out var value) ? value : null;

        if (options.EnvFilePath is not { } explicitEnv)
            return EnvFile.Load(Path.Combine(envDirectory, ".env"), EnvLookup, warnings);

        var full = Path.GetFullPath(explicitEnv);
        if (!File.Exists(full))
            throw new ComposeLoadException($"--env-file not found: {full}");

        return EnvFile.Load(full, EnvLookup, warnings);
    }

    private static HashSet<string> BuildActiveProfiles(ComposeLoadOptions options, IReadOnlyDictionary<string, string> pool)
    {
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in options.Profiles)
        {
            if (!string.IsNullOrWhiteSpace(profile))
                active.Add(profile.Trim());
        }

        if (pool.TryGetValue("COMPOSE_PROFILES", out var fromEnv) && !string.IsNullOrWhiteSpace(fromEnv))
        {
            foreach (var part in fromEnv.Split(','))
            {
                if (!string.IsNullOrWhiteSpace(part))
                    active.Add(part.Trim());
            }
        }

        return active;
    }

    /// <summary>Adds the profiles of any explicitly-targeted service to the active set (targeting a service enables its profile).</summary>
    private static void AddTargetedServiceProfiles(object? merged, IReadOnlyList<string> targeted, HashSet<string> active)
    {
        if (targeted.Count == 0)
            return;

        var services = TryGetServicesMap(merged);
        if (services is null)
            return;

        foreach (var name in targeted)
        {
            if (!services.TryGetValue(name, out var service))
                continue;

            var profiles = TryGetProfilesList(service);
            if (profiles is null)
                continue;

            foreach (var profile in profiles)
            {
                if (profile is not null)
                    active.Add(Convert.ToString(profile) ?? string.Empty);
            }
        }
    }

    private static IDictionary<string, object?>? TryGetServicesMap(object? merged)
    {
        if (YamlGraph.AsMap(merged) is not { } root)
            return null;

        return YamlGraph.AsMap(root.TryGetValue("services", out var s) ? s : null);
    }

    private static IList<object?>? TryGetProfilesList(object? service)
    {
        if (YamlGraph.AsMap(service) is not { } serviceMap)
            return null;

        return YamlGraph.AsList(serviceMap.TryGetValue("profiles", out var p) ? p : null);
    }

    private static void Overlay(Dictionary<string, string> pool, IEnumerable<KeyValuePair<string, string>> values)
    {
        foreach (var kvp in values)
            pool[kvp.Key] = kvp.Value;
    }

    private static Dictionary<string, string> CaptureProcessEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = Convert.ToString(entry.Key);
            if (!string.IsNullOrEmpty(key))
                result[key] = Convert.ToString(entry.Value) ?? string.Empty;
        }

        return result;
    }
}

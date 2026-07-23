namespace Wslcc.Compose;

/// <summary>
/// Selects which Compose file(s) to load, mirroring docker-compose precedence: explicit <c>-f</c>
/// values first, then the <c>COMPOSE_FILE</c> environment variable (split on <c>COMPOSE_PATH_SEPARATOR</c>,
/// defaulting to the OS path separator), then the conventional file names in the search directory.
/// </summary>
public static class ComposeFileDiscovery
{
    private static readonly string[] Candidates =
    {
        "compose.yaml", "compose.yml", "docker-compose.yaml", "docker-compose.yml",
    };

    /// <summary>Returns the absolute compose file paths to load, or an empty list when none are found.</summary>
    public static IReadOnlyList<string> Discover(
        IReadOnlyList<string> specified,
        string searchDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        if (specified.Count > 0)
        {
            return ResolveExplicit(specified, searchDirectory);
        }

        if (environment.TryGetValue("COMPOSE_FILE", out var composeFile) && !string.IsNullOrWhiteSpace(composeFile))
        {
            var separator = environment.TryGetValue("COMPOSE_PATH_SEPARATOR", out var custom) && custom.Length > 0
                ? custom[0]
                : Path.PathSeparator;
            var parts = composeFile.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return ResolveExplicit(parts, searchDirectory);
        }

        foreach (var candidate in Candidates)
        {
            var path = Path.Combine(searchDirectory, candidate);
            if (File.Exists(path))
            {
                return new[] { Path.GetFullPath(path) };
            }
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyList<string> ResolveExplicit(IReadOnlyList<string> names, string searchDirectory)
    {
        var result = new List<string>(names.Count);
        foreach (var name in names)
        {
            var path = Path.IsPathRooted(name) ? Path.GetFullPath(name) : Path.GetFullPath(Path.Combine(searchDirectory, name));
            if (!File.Exists(path))
            {
                throw new ComposeLoadException($"Compose file not found: {path}");
            }

            result.Add(path);
        }

        return result;
    }
}

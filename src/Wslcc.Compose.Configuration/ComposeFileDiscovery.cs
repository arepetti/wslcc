namespace Wslcc.Compose.Configuration;

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
    /// <param name="specified">Files named with <c>-f</c>, in override order; empty to fall back to the other sources.</param>
    /// <param name="searchDirectory">Directory relative names resolve against and conventional names are looked for in.</param>
    /// <param name="environment">Environment to read <c>COMPOSE_FILE</c> and <c>COMPOSE_PATH_SEPARATOR</c> from.</param>
    /// <returns>The absolute paths to load in override order, or an empty list when no compose file was found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="specified"/> or <paramref name="environment"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="searchDirectory"/> is null, empty or whitespace.</exception>
    /// <exception cref="ComposeLoadException">An explicitly named file (from <c>-f</c> or <c>COMPOSE_FILE</c>) does not exist.</exception>
    public static IReadOnlyList<string> Discover(
        IReadOnlyList<string> specified,
        string searchDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(specified);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchDirectory);
        ArgumentNullException.ThrowIfNull(environment);

        if (specified.Count > 0)
            return ResolveExplicit(specified, searchDirectory);

        var composeFile = environment.TryGetValue("COMPOSE_FILE", out var fromEnvironment) ? fromEnvironment : null;
        if (!string.IsNullOrWhiteSpace(composeFile))
        {
            var separator = ResolvePathSeparator(environment);
            var parts = composeFile.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return ResolveExplicit(parts, searchDirectory);
        }

        foreach (var candidate in Candidates)
        {
            var path = Path.Combine(searchDirectory, candidate);
            if (File.Exists(path))
                return new[] { Path.GetFullPath(path) };
        }

        return Array.Empty<string>();
    }

    /// <summary>The separator <c>COMPOSE_FILE</c> is split on: <c>COMPOSE_PATH_SEPARATOR</c>, else the OS default.</summary>
    private static char ResolvePathSeparator(IReadOnlyDictionary<string, string> environment)
        => environment.TryGetValue("COMPOSE_PATH_SEPARATOR", out var custom) && custom.Length > 0
            ? custom[0]
            : Path.PathSeparator;

    private static IReadOnlyList<string> ResolveExplicit(IReadOnlyList<string> names, string searchDirectory)
    {
        var result = new List<string>(names.Count);
        foreach (var name in names)
        {
            var path = Path.IsPathRooted(name) ? Path.GetFullPath(name) : Path.GetFullPath(Path.Combine(searchDirectory, name));
            if (!File.Exists(path))
                throw new ComposeLoadException($"Compose file not found: {path}");

            result.Add(path);
        }

        return result;
    }
}

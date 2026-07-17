namespace Wslcc.Cli;

/// <summary>Locates the <c>wslccd</c> executable for <c>wslcc daemon start</c>.</summary>
internal static class DaemonLocator
{
    /// <summary>
    /// Resolution order:
    /// <list type="number">
    /// <item>the <c>WSLCCD_PATH</c> environment variable;</item>
    /// <item><c>wslccd(.exe)</c> next to the CLI executable (published/installed layout);</item>
    /// <item>the sibling project output in the .NET artifacts layout used during development
    /// (<c>src/out/bin/Wslcc.Cli/&lt;config&gt;</c> → <c>src/out/bin/Wslccd/&lt;config&gt;</c>).</item>
    /// </list>
    /// </summary>
    public static string? Find()
    {
        var fromEnv = Environment.GetEnvironmentVariable("WSLCCD_PATH");
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        var baseDir = AppContext.BaseDirectory;

        // Published/installed layout: wslccd sits next to wslcc.
        if (FindIn(baseDir) is { } sideBySide)
        {
            return sideBySide;
        }

        // Dev artifacts layout: .../bin/Wslcc.Cli/<config> -> .../bin/Wslccd/<config>.
        var configDir = new DirectoryInfo(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var binRoot = configDir.Parent?.Parent;
        if (binRoot is not null)
        {
            var sibling = Path.Combine(binRoot.FullName, "Wslccd", configDir.Name);
            if (FindIn(sibling) is { } fromArtifacts)
            {
                return fromArtifacts;
            }
        }

        return null;
    }

    private static string? FindIn(string directory)
    {
        foreach (var name in new[] { "wslccd.exe", "wslccd" })
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

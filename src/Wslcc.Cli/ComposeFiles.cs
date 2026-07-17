namespace Wslcc.Cli;

/// <summary>Resolved Compose file inputs to send to the daemon.</summary>
internal sealed record ComposeInputs(string Yaml, string DefaultProjectName, string FilePath);

/// <summary>Locates and reads the Compose file (explicit <c>--file</c> or conventional names).</summary>
internal static class ComposeFiles
{
    private static readonly string[] Candidates =
    {
        "compose.yaml", "compose.yml", "docker-compose.yaml", "docker-compose.yml",
    };

    /// <summary>Returns the resolved inputs, or <c>null</c> if no file was found.</summary>
    public static ComposeInputs? Resolve(string? file)
    {
        if (!string.IsNullOrWhiteSpace(file))
        {
            var full = Path.GetFullPath(file!);
            return File.Exists(full)
                ? new ComposeInputs(File.ReadAllText(full), DirectoryName(full), full)
                : null;
        }

        var cwd = Directory.GetCurrentDirectory();
        foreach (var candidate in Candidates)
        {
            var path = Path.Combine(cwd, candidate);
            if (File.Exists(path))
            {
                return new ComposeInputs(File.ReadAllText(path), DirectoryName(path), path);
            }
        }

        return null;
    }

    private static string DirectoryName(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        return string.IsNullOrEmpty(dir) ? "wslcc" : new DirectoryInfo(dir).Name;
    }
}

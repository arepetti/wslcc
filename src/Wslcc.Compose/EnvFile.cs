namespace Wslcc.Compose;

/// <summary>
/// Minimal <c>.env</c> reader: <c>KEY=VALUE</c> lines, <c>#</c> comments, blank lines ignored, an
/// optional leading <c>export</c>, and surrounding single/double quotes stripped from the value.
/// This is deliberately a small subset of the docker-compose <c>.env</c> format (no multiline values,
/// no in-value variable expansion); the remaining fidelity is tracked in docs/todo.md.
/// </summary>
public static class EnvFile
{
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line.Substring("export ".Length).TrimStart();
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue; // no key, or a bare token: skip rather than fail
            }

            var key = line.Substring(0, eq).Trim();
            var value = line.Substring(eq + 1).Trim();
            result[key] = Unquote(value);
        }

        return result;
    }

    /// <summary>Reads and parses the file at <paramref name="path"/>, or returns empty if it does not exist.</summary>
    public static IReadOnlyDictionary<string, string> Load(string path)
        => File.Exists(path)
            ? Parse(File.ReadAllText(path))
            : new Dictionary<string, string>(StringComparer.Ordinal);

    private static string Unquote(string value)
    {
        if (value.Length >= 2)
        {
            var first = value[0];
            var last = value[value.Length - 1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
            {
                return value.Substring(1, value.Length - 2);
            }
        }

        return value;
    }
}

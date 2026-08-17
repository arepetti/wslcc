using System.Text;
using Wslcc.Abstractions.Compose;

namespace Wslcc.Compose;

/// <summary>Resolves and sanitizes Compose project names. Shared by the CLI and the daemon.</summary>
// Compose file — name: the document's project name, which an explicit -p still overrides.
public static class ProjectNames
{
    /// <summary>
    /// Effective project name: explicit value wins, else the compose file's <c>name:</c>, else the
    /// caller-provided default (typically the directory name). The result is sanitized.
    /// </summary>
    /// <param name="explicitName">Name given on the command line (<c>-p</c>), if any.</param>
    /// <param name="file">The compose file, whose top-level <c>name:</c> is the next candidate.</param>
    /// <param name="defaultName">Fallback name, typically the project directory's name.</param>
    /// <returns>The sanitized project name; <c>"wslcc"</c> when nothing usable was supplied.</returns>
    public static string Resolve(string? explicitName, ComposeFile? file, string? defaultName)
    {
        var chosen = FirstNonEmpty(explicitName, file?.Name, defaultName, "wslcc");
        return Sanitize(chosen);
    }

    /// <summary>
    /// Like <see cref="Resolve"/> but returns <c>null</c> when nothing identifies a project (no explicit
    /// name, no file name, no default). Used by read/scoping operations where "unspecified" means
    /// "across all projects".
    /// </summary>
    /// <param name="explicitName">Name given on the command line (<c>-p</c>), if any.</param>
    /// <param name="file">The compose file, whose top-level <c>name:</c> is the next candidate.</param>
    /// <param name="defaultName">Fallback name, typically the project directory's name.</param>
    /// <returns>The sanitized project name, or <c>null</c> when nothing identifies a project.</returns>
    public static string? ResolveOrNull(string? explicitName, ComposeFile? file, string? defaultName)
    {
        foreach (var value in new[] { explicitName, file?.Name, defaultName })
        {
            if (!string.IsNullOrWhiteSpace(value))
                return Sanitize(value!);
        }

        return null;
    }

    /// <summary>Normalizes a name to lowercase alphanumerics, dashes and underscores.</summary>
    /// <param name="name">The raw name; spaces and dots become underscores and anything else is dropped.</param>
    /// <returns>The sanitized name, or <c>"wslcc"</c> when nothing usable is left.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <c>null</c>.</exception>
    public static string Sanitize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (IsAllowedNameChar(c))
                sb.Append(c);
            else if (c == ' ' || c == '.')
                sb.Append('_');
        }

        var result = sb.ToString().Trim('_', '-');
        return result.Length == 0 ? "wslcc" : result;
    }

    private static bool IsAllowedNameChar(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_';

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value!;
        }

        return "wslcc";
    }
}

using System.Text;
using Wslcc.Abstractions.Compose;

namespace Wslcc.Core;

/// <summary>Resolves and sanitizes Compose project names.</summary>
public static class ProjectNames
{
    /// <summary>
    /// Effective project name: explicit value wins, else the compose file's <c>name:</c>, else the
    /// caller-provided default (typically the directory name). The result is sanitized.
    /// </summary>
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
    public static string? ResolveOrNull(string? explicitName, ComposeFile? file, string? defaultName)
    {
        foreach (var value in new[] { explicitName, file?.Name, defaultName })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return Sanitize(value!);
            }
        }

        return null;
    }

    /// <summary>Normalizes a name to lowercase alphanumerics, dashes and underscores.</summary>
    public static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
            {
                sb.Append(c);
            }
            else if (c == ' ' || c == '.')
            {
                sb.Append('_');
            }
        }

        var result = sb.ToString().Trim('_', '-');
        return result.Length == 0 ? "wslcc" : result;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!;
            }
        }

        return "wslcc";
    }
}

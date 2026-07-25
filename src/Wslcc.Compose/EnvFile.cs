using System.Text;

namespace Wslcc.Compose;

/// <summary>
/// Reads a docker-compose-style <c>.env</c> file. Supports <c>KEY=VALUE</c> lines, an optional leading
/// <c>export</c>, <c>#</c> comments (whole-line and inline for unquoted values), and:
/// <list type="bullet">
/// <item>single-quoted values — taken literally (no escapes, no interpolation);</item>
/// <item>double-quoted values — with C-style escapes (<c>\n</c>, <c>\t</c>, <c>\r</c>, <c>\f</c>,
/// <c>\b</c>, <c>\v</c>, <c>\"</c>, <c>\\</c>) and <c>${VAR}</c> interpolation;</item>
/// <item>unquoted values — trimmed, with <c>${VAR}</c> interpolation and an inline <c># comment</c>;</item>
/// <item>multi-line quoted values spanning physical lines until the closing quote.</item>
/// </list>
/// Interpolation (for unquoted and double-quoted values) resolves against variables defined earlier in
/// the same file first, then a caller-supplied <c>lookup</c> (the process environment). Use <c>$$</c> for
/// a literal <c>$</c>.
/// </summary>
public static class EnvFile
{
    /// <summary>
    /// Parses <paramref name="text"/>. <paramref name="lookup"/> resolves <c>${VAR}</c> references that
    /// are not defined earlier in the file (typically the process environment); interpolation warnings
    /// (e.g. an unset variable) are appended to <paramref name="warnings"/> when provided.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Parse(
        string text,
        Func<string, string?>? lookup = null,
        List<string>? warnings = null)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        var interpolator = new VariableInterpolator(
            name => result.TryGetValue(name, out var value) ? value : lookup?.Invoke(name),
            warnings);

        var s = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var pos = 0;

        while (pos < s.Length)
        {
            SkipBlankAndNewlines(s, ref pos);
            if (pos >= s.Length)
            {
                break;
            }

            if (s[pos] == '#')
            {
                pos = LineEnd(s, pos);
                continue;
            }

            TryConsumeExport(s, ref pos);

            var keyStart = pos;
            while (pos < s.Length && s[pos] != '=' && s[pos] != '\n')
            {
                pos++;
            }

            // A line with no '=' is not an assignment (a bare token); skip it rather than fail.
            if (pos >= s.Length || s[pos] == '\n')
            {
                continue;
            }

            var key = s.Substring(keyStart, pos - keyStart).Trim();
            pos++; // consume '='

            if (key.Length == 0)
            {
                pos = LineEnd(s, pos);
                continue;
            }

            result[key] = ReadValue(s, ref pos, interpolator);
        }

        return result;
    }

    /// <summary>Reads and parses the file at <paramref name="path"/>, or returns empty if it does not exist.</summary>
    public static IReadOnlyDictionary<string, string> Load(
        string path,
        Func<string, string?>? lookup = null,
        List<string>? warnings = null)
        => File.Exists(path)
            ? Parse(File.ReadAllText(path), lookup, warnings)
            : new Dictionary<string, string>(StringComparer.Ordinal);

    private static string ReadValue(string s, ref int pos, VariableInterpolator interpolator)
    {
        // Leading whitespace before the value is insignificant (and lets us detect a leading quote).
        while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t'))
        {
            pos++;
        }

        if (pos < s.Length && s[pos] == '"')
        {
            var inner = ReadQuoted(s, ref pos, '"', allowEscapes: true, out var terminated);
            if (!terminated)
            {
                throw new ComposeLoadException("Unterminated double-quoted value in env file.");
            }

            pos = LineEnd(s, pos); // ignore anything trailing the closing quote (e.g. a comment)
            return interpolator.Interpolate(Unescape(inner));
        }

        if (pos < s.Length && s[pos] == '\'')
        {
            var inner = ReadQuoted(s, ref pos, '\'', allowEscapes: false, out var terminated);
            if (!terminated)
            {
                throw new ComposeLoadException("Unterminated single-quoted value in env file.");
            }

            pos = LineEnd(s, pos);
            return inner; // single-quoted values are literal: no escapes, no interpolation
        }

        var start = pos;
        var end = LineEnd(s, pos);
        pos = end;
        return interpolator.Interpolate(StripInlineComment(s.Substring(start, end - start)).TrimEnd());
    }

    /// <summary>
    /// Reads a quoted value starting at the opening quote (<paramref name="pos"/>), spanning newlines
    /// until the matching close. For double quotes a backslash escapes the next character (so <c>\"</c>
    /// does not close); the raw escape sequences are preserved for <see cref="Unescape"/>.
    /// </summary>
    private static string ReadQuoted(string s, ref int pos, char quote, bool allowEscapes, out bool terminated)
    {
        pos++; // skip the opening quote
        var sb = new StringBuilder();
        while (pos < s.Length)
        {
            var c = s[pos];
            if (allowEscapes && c == '\\' && pos + 1 < s.Length)
            {
                sb.Append(c);
                sb.Append(s[pos + 1]);
                pos += 2;
                continue;
            }

            if (c == quote)
            {
                pos++; // consume the closing quote
                terminated = true;
                return sb.ToString();
            }

            sb.Append(c);
            pos++;
        }

        terminated = false;
        return sb.ToString();
    }

    /// <summary>Resolves C-style backslash escapes in a double-quoted value; unknown escapes are kept verbatim.</summary>
    private static string Unescape(string value)
    {
        if (value.IndexOf('\\') < 0)
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c != '\\' || i + 1 >= value.Length)
            {
                sb.Append(c);
                continue;
            }

            switch (value[++i])
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'f': sb.Append('\f'); break;
                case 'b': sb.Append('\b'); break;
                case 'v': sb.Append('\v'); break;
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                default:
                    sb.Append('\\');
                    sb.Append(value[i]);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>Trims an inline comment (a <c>#</c> at the value start or following whitespace) from an unquoted value.</summary>
    private static string StripInlineComment(string raw)
    {
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '#' && (i == 0 || raw[i - 1] == ' ' || raw[i - 1] == '\t'))
            {
                return raw.Substring(0, i);
            }
        }

        return raw;
    }

    private static void SkipBlankAndNewlines(string s, ref int pos)
    {
        while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\n'))
        {
            pos++;
        }
    }

    private static int LineEnd(string s, int pos)
    {
        var newline = s.IndexOf('\n', pos);
        return newline < 0 ? s.Length : newline;
    }

    private static void TryConsumeExport(string s, ref int pos)
    {
        const string keyword = "export";
        if (pos + keyword.Length < s.Length
            && string.CompareOrdinal(s, pos, keyword, 0, keyword.Length) == 0
            && (s[pos + keyword.Length] == ' ' || s[pos + keyword.Length] == '\t'))
        {
            pos += keyword.Length;
            while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t'))
            {
                pos++;
            }
        }
    }
}

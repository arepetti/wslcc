using System.Text;

namespace Wslcc.Compose;

/// <summary>
/// Substitutes <c>$VAR</c> / <c>${VAR}</c> references in scalar values, matching docker-compose
/// interpolation. Supported forms:
/// <list type="bullet">
/// <item><c>$$</c> — a literal <c>$</c>.</item>
/// <item><c>$VAR</c>, <c>${VAR}</c> — the variable's value, or an empty string (with a warning) when unset.</item>
/// <item><c>${VAR:-default}</c> / <c>${VAR-default}</c> — default when empty-or-unset / unset.</item>
/// <item><c>${VAR:?err}</c> / <c>${VAR?err}</c> — fail with <c>err</c> when empty-or-unset / unset.</item>
/// <item><c>${VAR:+alt}</c> / <c>${VAR+alt}</c> — <c>alt</c> when set-and-non-empty / set (else empty).</item>
/// </list>
/// Default/alternate/error words may themselves reference variables and are interpolated recursively.
/// </summary>
public sealed class VariableInterpolator
{
    private readonly Func<string, string?> _lookup;
    private readonly List<string> _warnings;

    public VariableInterpolator(Func<string, string?> lookup, List<string>? warnings = null)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
        _warnings = warnings ?? new List<string>();
    }

    public IReadOnlyList<string> Warnings => _warnings;

    public string Interpolate(string input)
    {
        if (string.IsNullOrEmpty(input) || input.IndexOf('$') < 0)
        {
            return input;
        }

        var sb = new StringBuilder(input.Length);
        var i = 0;
        while (i < input.Length)
        {
            var c = input[i];
            if (c != '$')
            {
                sb.Append(c);
                i++;
                continue;
            }

            // c == '$'
            if (i + 1 >= input.Length)
            {
                sb.Append('$');
                break;
            }

            var next = input[i + 1];
            if (next == '$')
            {
                sb.Append('$');
                i += 2;
            }
            else if (next == '{')
            {
                var end = FindClosingBrace(input, i + 2);
                if (end < 0)
                {
                    throw new ComposeLoadException($"Invalid interpolation '{input.Substring(i)}': missing closing '}}'.");
                }

                var expr = input.Substring(i + 2, end - (i + 2));
                sb.Append(EvaluateBraced(expr));
                i = end + 1;
            }
            else if (IsNameStart(next))
            {
                var start = i + 1;
                var j = start;
                while (j < input.Length && IsNameChar(input[j]))
                {
                    j++;
                }

                var name = input.Substring(start, j - start);
                sb.Append(ResolvePlain(name));
                i = j;
            }
            else
            {
                sb.Append('$');
                i++;
            }
        }

        return sb.ToString();
    }

    private string EvaluateBraced(string expr)
    {
        var nameEnd = 0;
        while (nameEnd < expr.Length && IsNameChar(expr[nameEnd]))
        {
            nameEnd++;
        }

        if (nameEnd == 0)
        {
            throw new ComposeLoadException($"Invalid interpolation '${{{expr}}}': missing variable name.");
        }

        var name = expr.Substring(0, nameEnd);
        var rest = expr.Substring(nameEnd);
        if (rest.Length == 0)
        {
            return ResolvePlain(name);
        }

        var treatEmptyAsUnset = rest[0] == ':';
        var opIndex = treatEmptyAsUnset ? 1 : 0;
        if (opIndex >= rest.Length)
        {
            throw new ComposeLoadException($"Invalid interpolation '${{{expr}}}'.");
        }

        var op = rest[opIndex];
        var word = rest.Substring(opIndex + 1);
        var value = _lookup(name);
        var isSet = value is not null && (!treatEmptyAsUnset || value.Length > 0);

        switch (op)
        {
            case '-':
                return isSet ? value! : Interpolate(word);
            case '+':
                return isSet ? Interpolate(word) : string.Empty;
            case '?':
                if (isSet)
                {
                    return value!;
                }

                throw new ComposeLoadException(word.Length > 0
                    ? $"Required variable '{name}' is not set: {Interpolate(word)}"
                    : $"Required variable '{name}' is not set.");
            default:
                throw new ComposeLoadException($"Invalid interpolation operator in '${{{expr}}}'.");
        }
    }

    private string ResolvePlain(string name)
    {
        var value = _lookup(name);
        if (value is not null)
        {
            return value;
        }

        _warnings.Add($"The \"{name}\" variable is not set. Defaulting to a blank string.");
        return string.Empty;
    }

    /// <summary>Finds the <c>}</c> matching the <c>${</c> whose content starts at <paramref name="contentStart"/>, allowing nested <c>${...}</c> in default/alternate words.</summary>
    private static int FindClosingBrace(string input, int contentStart)
    {
        var depth = 0;
        for (var k = contentStart; k < input.Length; k++)
        {
            var ch = input[k];
            if (ch == '$' && k + 1 < input.Length && input[k + 1] == '{')
            {
                depth++;
                k++;
            }
            else if (ch == '}')
            {
                if (depth == 0)
                {
                    return k;
                }

                depth--;
            }
        }

        return -1;
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}

namespace Wslcc.Cli;

/// <summary>
/// Builds argument strings for <c>reg.exe</c> to manage the per-user autostart entry used by
/// <c>wslcc daemon install</c>/<c>uninstall</c>: a value under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> that launches <c>wslccd</c> in the user's
/// session at every logon. HKCU is writable without elevation, so this is a genuine per-user autostart
/// (no Windows Service, no admin). Kept pure (no process invocation) so the nested quoting the
/// <c>/d</c> value needs — the executable path is quoted so <c>CreateProcess</c> keeps it as one token,
/// and the whole value is quoted again so it reaches <c>reg.exe</c> as a single argument — can be
/// reasoned about, and tested, in isolation.
/// </summary>
public static class AutostartCommandBuilder
{
    /// <summary>Per-user "run at logon" key. Writable without elevation.</summary>
    public const string RunKeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary><c>reg add</c> for the Run value. <c>/f</c> overwrites so re-running install is idempotent.</summary>
    public static string BuildAddArguments(string valueName, string executablePath, IReadOnlyList<string> executableArgs)
    {
        var data = BuildCommandValue(executablePath, executableArgs);
        return $"add {Quote(RunKeyPath)} /v {Quote(valueName)} /t REG_SZ /d {data} /f";
    }

    public static string BuildDeleteArguments(string valueName)
        => $"delete {Quote(RunKeyPath)} /v {Quote(valueName)} /f";

    /// <summary>
    /// Builds the <c>/d</c> command line: the executable path is wrapped in escaped quotes so it stays a
    /// single token when Windows launches the entry (even with spaces), and the whole value is wrapped
    /// again so it reaches <c>reg.exe</c> as one argument.
    /// </summary>
    private static string BuildCommandValue(string executablePath, IReadOnlyList<string> executableArgs)
    {
        var inner = $"\\\"{executablePath}\\\"";
        if (executableArgs.Count > 0)
        {
            inner = $"{inner} {string.Join(' ', executableArgs)}";
        }

        return $"\"{inner}\"";
    }

    private static string Quote(string value) => $"\"{value}\"";
}

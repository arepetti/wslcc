namespace Wslcc.Cli;

/// <summary>
/// Builds argument strings for <c>sc.exe</c> (the Windows Service Control command), used by
/// <c>wslcc daemon install</c>/<c>uninstall</c>. Kept separate and pure (no process invocation) so the
/// <c>key= value</c> syntax (<c>sc.exe</c> requires a literal space after each <c>key=</c>) and the
/// nested quoting it needs for a <c>binPath=</c> can be reasoned about, and tested, in isolation.
/// </summary>
public static class ServiceControlCommandBuilder
{
    public static string BuildCreateArguments(
        string serviceName, string executablePath, IReadOnlyList<string> executableArgs, string startupType, string displayName)
    {
        var binPath = BuildBinPathValue(executablePath, executableArgs);
        return $"create {Quote(serviceName)} binPath= {binPath} start= {startupType} DisplayName= {Quote(displayName)}";
    }

    public static string BuildDescriptionArguments(string serviceName, string description)
        => $"description {Quote(serviceName)} {Quote(description)}";

    public static string BuildDeleteArguments(string serviceName) => $"delete {Quote(serviceName)}";

    public static string BuildStopArguments(string serviceName) => $"stop {Quote(serviceName)}";

    /// <summary>
    /// Builds the <c>binPath=</c> value: the executable path is wrapped in escaped quotes so the Service
    /// Control Manager can tell it apart from trailing arguments even when the path contains spaces, and
    /// the whole value is wrapped again so it becomes a single argument to <c>sc.exe</c> itself.
    /// </summary>
    private static string BuildBinPathValue(string executablePath, IReadOnlyList<string> executableArgs)
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

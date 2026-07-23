namespace Wslcc.Abstractions;

/// <summary>
/// Identifiers shared between the daemon's own Windows Service registration (<c>Wslccd/Program.cs</c>'s
/// <c>UseWindowsService</c> call) and the CLI's <c>wslcc daemon install</c>/<c>uninstall</c> commands, so
/// both agree on the same Service Control Manager name.
/// </summary>
public static class WslccdConstants
{
    /// <summary>Windows Service name used to register/control <c>wslccd</c> via <c>sc.exe</c>.</summary>
    public const string ServiceName = "WSLCC Daemon";
}

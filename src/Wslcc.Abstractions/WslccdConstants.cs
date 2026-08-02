namespace Wslcc.Abstractions;

/// <summary>
/// Identifiers shared between the daemon (<c>Wslccd/Program.cs</c>) and the CLI's
/// <c>wslcc daemon install</c>/<c>uninstall</c> commands, so both agree on the same names.
/// </summary>
public static class WslccdConstants
{
    /// <summary>
    /// Windows Service name used by <c>UseWindowsService</c>. The CLI no longer registers a service
    /// (it uses a per-user HKCU Run autostart entry, see <see cref="AutostartName"/>), but the daemon
    /// keeps this name so an advanced user who registers it manually with <c>sc.exe</c> still gets a
    /// friendly name.
    /// </summary>
    public const string ServiceName = "WSLCC Daemon";

    /// <summary>
    /// Name of the per-user autostart entry (a value under <c>HKCU\...\Run</c>) that
    /// <c>wslcc daemon install</c>/<c>uninstall</c> uses to start <c>wslccd</c> in the user's session at
    /// logon — a per-user autostart, no elevation and no Windows Service.
    /// </summary>
    public const string AutostartName = "WSLCC Daemon";
}

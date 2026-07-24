using System.ComponentModel;
using Spectre.Console.Cli;

namespace Wslcc.Cli;

/// <summary>
/// Adds the daemon endpoint option for commands that talk to (or manage) <c>wslccd</c> directly:
/// the top-level <c>version</c> and <c>daemon status</c>/<c>stop</c>/<c>start</c>/<c>install</c>.
/// These are not <c>docker compose</c> commands, so the option keeps its plain <c>--host</c> name.
/// </summary>
public class HostSettings : GlobalSettings
{
    [CommandOption("-H|--host <URI>")]
    [Description("Daemon endpoint. npipe://<name> (default) for a local pipe, or http(s)://host:port for a remote daemon.")]
    public string? Host { get; set; }
}

/// <summary>
/// Adds the provider option for the daemon-management commands that persist the daemon's default
/// provider (<c>daemon start</c>, <c>daemon install</c>). Here <c>--provider</c> configures the daemon's
/// default rather than targeting a single call, so — unlike the compose commands, which use
/// <c>--wslcc-provider</c> — it keeps the plain name.
/// </summary>
public class DaemonProviderSettings : HostSettings
{
    [CommandOption("--provider <NAME>")]
    [Description("Provider to make the daemon's default: 'wslc' or 'docker'.")]
    public string? Provider { get; set; }
}

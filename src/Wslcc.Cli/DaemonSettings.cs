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
    [Description("Daemon endpoint. npipe://<name> (default) for a local pipe, or https://host:port for a remote daemon.")]
    public string? Host { get; set; }

    [CommandOption("--token <TOKEN>")]
    [Description("Bearer token for https:// endpoints. Defaults to the WSLCC_TOKEN environment variable.")]
    public string? Token { get; set; }

    [CommandOption("--tls-ca <PATH>")]
    [Description("PEM file of the extra CA or self-signed server certificate to trust. Defaults to WSLCC_TLS_CA.")]
    public string? TlsCa { get; set; }
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

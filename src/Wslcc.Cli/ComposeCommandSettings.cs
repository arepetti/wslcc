using System.ComponentModel;
using Spectre.Console.Cli;

namespace Wslcc.Cli;

/// <summary>
/// Compose-file options shared by every compose command, including the client-side <c>config</c>. These
/// mirror <c>docker compose</c>'s own file/project options, so they keep the standard names.
/// </summary>
public class ComposeFileSettings : GlobalSettings
{
    [CommandOption("-f|--file <PATH>")]
    [Description("Compose file(s). Repeatable; later files override earlier ones. Defaults to compose.yaml / docker-compose.yml in the current directory.")]
    public string[] Files { get; set; } = Array.Empty<string>();

    [CommandOption("-p|--project-name <NAME>")]
    [Description("Project name. Defaults to the compose file's 'name:' or the directory name.")]
    public string? ProjectName { get; set; }

    [CommandOption("--profile <NAME>")]
    [Description("Profile(s) to activate. Repeatable; also read from COMPOSE_PROFILES.")]
    public string[] Profiles { get; set; } = Array.Empty<string>();

    [CommandOption("--env-file <PATH>")]
    [Description("Environment file for variable interpolation. Defaults to '.env' in the project directory when present.")]
    public string? EnvFile { get; set; }

    [CommandOption("--project-directory <PATH>")]
    [Description("Alternate project directory (default: the first compose file's directory). Sets the default '.env' location, build context base, and project name.")]
    public string? ProjectDirectory { get; set; }
}

/// <summary>
/// Adds the daemon endpoint and per-command provider selection for the compose commands that reach the
/// daemon (everything except the client-side <c>config</c>). These use the <c>--wslcc-</c> prefix so they
/// never collide with a real <c>docker compose</c> option — wslcc aims to mirror the compose CLI, and
/// <c>--host</c>/<c>--provider</c> are wslcc-specific, not standard compose flags.
/// </summary>
public class ComposeCommandSettings : ComposeFileSettings
{
    [CommandOption("--wslcc-host <URI>")]
    [Description("Daemon endpoint. npipe://<name> (default) for a local pipe, or http(s)://host:port for a remote daemon.")]
    public string? Host { get; set; }

    [CommandOption("--wslcc-provider <NAME>")]
    [Description("Provider to target for this command: 'wslc' or 'docker'. Defaults to the daemon's configured provider.")]
    public string? Provider { get; set; }
}

using System.ComponentModel;
using Spectre.Console.Cli;

namespace Wslcc.Cli;

/// <summary>Shared options for compose commands that operate on a project/file.</summary>
public class ComposeCommandSettings : GlobalSettings
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

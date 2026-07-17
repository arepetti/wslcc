using System.ComponentModel;
using Spectre.Console.Cli;

namespace Wslcc.Cli;

/// <summary>Shared options for compose commands that operate on a project/file.</summary>
public class ComposeCommandSettings : GlobalSettings
{
    [CommandOption("-f|--file <PATH>")]
    [Description("Path to the Compose file. Defaults to compose.yaml / docker-compose.yml in the current directory.")]
    public string? File { get; set; }

    [CommandOption("-p|--project-name <NAME>")]
    [Description("Project name. Defaults to the compose file's 'name:' or the directory name.")]
    public string? ProjectName { get; set; }
}

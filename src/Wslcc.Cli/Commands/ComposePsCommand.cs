using System.ComponentModel;
using Grpc.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using Wslcc.Client;
using Wslcc.Grpc.Contracts;

namespace Wslcc.Cli.Commands;

/// <summary><c>wslcc compose ps</c>: list the project's containers.</summary>
public sealed class ComposePsCommand : AsyncCommand<ComposePsCommand.Settings>
{
    public sealed class Settings : ComposeCommandSettings
    {
        [CommandOption("-a|--all")]
        [Description("Show stopped containers too.")]
        public bool All { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var inputs = ComposeFiles.Resolve(settings.File);

        var request = new PsRequest
        {
            ProjectName = settings.ProjectName ?? string.Empty,
            DefaultProjectName = inputs?.DefaultProjectName ?? string.Empty,
            ComposeYaml = inputs?.Yaml ?? string.Empty,
            Provider = settings.Provider ?? string.Empty,
            All = settings.All,
        };

        try
        {
            using var client = new WslccClient(settings.Host);
            var response = await client.PsAsync(request, cancellationToken).ConfigureAwait(false);

            var scoped = !string.IsNullOrEmpty(response.ProjectName);

            if (response.Containers.Count == 0)
            {
                AnsiConsole.MarkupLine(scoped
                    ? $"[grey]No containers for project '{response.ProjectName.EscapeMarkup()}'.[/]"
                    : "[grey]No wslcc-managed containers.[/]");
                return 0;
            }

            var table = new Table().RoundedBorder();

            // When scoped to one project the Project column is redundant.
            if (scoped)
            {
                table.AddColumns("Name", "Service", "Image", "State", "Status", "Ports");
            }
            else
            {
                table.AddColumns("Project", "Name", "Service", "Image", "State", "Status", "Ports");
            }

            foreach (var c in response.Containers.OrderBy(c => c.Project).ThenBy(c => c.Service))
            {
                if (scoped)
                {
                    table.AddRow(
                        c.Name.EscapeMarkup(),
                        c.Service.EscapeMarkup(),
                        c.Image.EscapeMarkup(),
                        c.State.EscapeMarkup(),
                        c.Status.EscapeMarkup(),
                        c.Ports.EscapeMarkup());
                }
                else
                {
                    table.AddRow(
                        c.Project.EscapeMarkup(),
                        c.Name.EscapeMarkup(),
                        c.Service.EscapeMarkup(),
                        c.Image.EscapeMarkup(),
                        c.State.EscapeMarkup(),
                        c.Status.EscapeMarkup(),
                        c.Ports.EscapeMarkup());
                }
            }

            AnsiConsole.Write(table);
            return 0;
        }
        catch (RpcException ex)
        {
            return RpcErrors.Report(ex);
        }
    }
}

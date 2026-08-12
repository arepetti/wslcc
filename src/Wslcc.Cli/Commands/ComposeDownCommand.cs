using System.ComponentModel;
using Grpc.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using Wslcc.Client;
using Wslcc.Grpc.Contracts;

namespace Wslcc.Cli.Commands;

/// <summary><c>wslcc compose down</c>: stop and remove the project's containers (and its networks).</summary>
public sealed class ComposeDownCommand : AsyncCommand<ComposeDownCommand.Settings>
{
    public sealed class Settings : ComposeCommandSettings
    {
        [CommandOption("-v|--volumes")]
        [Description("Also remove the project's named volumes. By default volumes are preserved.")]
        public bool Volumes { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!ComposeFiles.TryResolve(settings, out var inputs, out var loadError))
        {
            AnsiConsole.MarkupLine(loadError);
            return 1;
        }

        var request = new DownRequest
        {
            ProjectName = settings.ProjectName ?? string.Empty,
            DefaultProjectName = inputs?.DefaultProjectName ?? string.Empty,
            ComposeYaml = inputs?.Yaml ?? string.Empty,
            Provider = settings.Provider ?? string.Empty,
            Volumes = settings.Volumes,
        };

        if (inputs is null && string.IsNullOrWhiteSpace(settings.ProjectName))
        {
            AnsiConsole.MarkupLine("[red]No compose file found and no --project-name given.[/]");
            return 1;
        }

        try
        {
            using var client = new WslccClient(settings.Host);
            AnsiConsole.MarkupLine("[bold]Stopping services…[/]");
            var response = await client.DownAsync(request, ProgressDisplay.Create(), cancellationToken)
                .ConfigureAwait(false);

            AnsiConsole.MarkupLine($"[bold]Project:[/] {response.ProjectName.EscapeMarkup()}");

            if (response.Results.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No containers to remove.[/]");
                return 0;
            }

            return ServiceResults.FailedCount(response.Results) > 0 ? 1 : 0;
        }
        catch (RpcException ex)
        {
            return RpcErrors.Report(ex);
        }
    }
}

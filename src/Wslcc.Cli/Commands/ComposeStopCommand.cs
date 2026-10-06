using System.ComponentModel;
using Grpc.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using Wslcc.Client;
using Wslcc.Grpc.Contracts;

namespace Wslcc.Cli.Commands;

/// <summary><c>wslcc compose stop</c>: stop the project's containers without removing them.</summary>
public sealed class ComposeStopCommand : AsyncCommand<ComposeStopCommand.Settings>
{
    public sealed class Settings : ComposeCommandSettings
    {
        [CommandArgument(0, "[SERVICES]")]
        [Description("Services to stop. Defaults to every running service.")]
        public string[] Services { get; set; } = Array.Empty<string>();
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!ComposeFiles.TryResolve(settings, out var inputs, out var loadError, settings.Services))
        {
            AnsiConsole.MarkupLine(loadError);
            return 1;
        }

        if (inputs is null && string.IsNullOrWhiteSpace(settings.ProjectName))
        {
            AnsiConsole.MarkupLine("[red]No compose file found and no --project-name given.[/]");
            return 1;
        }

        var request = new StopRequest
        {
            ProjectName = settings.ProjectName ?? string.Empty,
            DefaultProjectName = inputs?.DefaultProjectName ?? string.Empty,
            ComposeYaml = inputs?.Yaml ?? string.Empty,
            Provider = settings.Provider ?? string.Empty,
        };
        request.Services.AddRange(settings.Services);

        try
        {
            using var client = DaemonConnection.Create(settings.Host, settings.Token, settings.TlsCa);
            AnsiConsole.MarkupLine("[bold]Stopping services…[/]");
            var response = await client.StopAsync(request, ProgressDisplay.Create(), cancellationToken)
                .ConfigureAwait(false);

            AnsiConsole.MarkupLine($"[bold]Project:[/] {response.ProjectName.EscapeMarkup()}");

            if (response.Results.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No containers to stop.[/]");
                return 0;
            }

            return ServiceResults.FailedCount(response.Results) > 0 ? 1 : 0;
        }
        catch (ArgumentException ex)
        {
            return RpcErrors.ReportSettings(ex);
        }
        catch (RpcException ex)
        {
            return RpcErrors.Report(ex);
        }
    }
}

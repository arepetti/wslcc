using System.ComponentModel;
using Grpc.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using Wslcc.Client;
using Wslcc.Grpc.Contracts;

namespace Wslcc.Cli.Commands;

/// <summary><c>wslcc compose restart</c>: restart the project's containers.</summary>
public sealed class ComposeRestartCommand : AsyncCommand<ComposeRestartCommand.Settings>
{
    public sealed class Settings : ComposeCommandSettings
    {
        [CommandArgument(0, "[SERVICES]")]
        [Description("Services to restart. Defaults to every service with an existing container.")]
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

        var request = new RestartRequest
        {
            ProjectName = settings.ProjectName ?? string.Empty,
            DefaultProjectName = inputs?.DefaultProjectName ?? string.Empty,
            ComposeYaml = inputs?.Yaml ?? string.Empty,
            Provider = settings.Provider ?? string.Empty,
        };
        request.Services.AddRange(settings.Services);

        try
        {
            using var client = new WslccClient(settings.Host);
            var response = await AnsiConsole.Status()
                .StartAsync("Restarting services...", async _ => await client.RestartAsync(request, cancellationToken))
                .ConfigureAwait(false);

            AnsiConsole.MarkupLine($"[bold]Project:[/] {response.ProjectName.EscapeMarkup()}");

            if (response.Results.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No containers to restart.[/]");
                return 0;
            }

            var failed = 0;
            foreach (var result in response.Results)
            {
                if (string.Equals(result.Status, "failed", StringComparison.OrdinalIgnoreCase))
                {
                    failed++;
                    AnsiConsole.MarkupLine($"  [red]x[/] {result.Service.EscapeMarkup()}: {result.Error.EscapeMarkup()}");
                }
                else
                {
                    AnsiConsole.MarkupLine($"  [green]~[/] {result.Service.EscapeMarkup()} ({result.Status.EscapeMarkup()})");
                }
            }

            return failed > 0 ? 1 : 0;
        }
        catch (RpcException ex)
        {
            return RpcErrors.Report(ex);
        }
    }
}

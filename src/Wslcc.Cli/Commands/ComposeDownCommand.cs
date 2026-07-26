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
            var response = await AnsiConsole.Status()
                .StartAsync("Stopping services...", async _ => await client.DownAsync(request, cancellationToken))
                .ConfigureAwait(false);

            AnsiConsole.MarkupLine($"[bold]Project:[/] {response.ProjectName.EscapeMarkup()}");

            if (response.Results.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No containers to remove.[/]");
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
                    AnsiConsole.MarkupLine($"  [green]-[/] {result.Service.EscapeMarkup()} ({result.Status.EscapeMarkup()})");
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

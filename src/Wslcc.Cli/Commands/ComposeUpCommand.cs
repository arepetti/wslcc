using System.ComponentModel;
using Grpc.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using Wslcc.Client;
using Wslcc.Grpc.Contracts;

namespace Wslcc.Cli.Commands;

/// <summary><c>wslcc compose up</c>: create and start the project's services (detached).</summary>
public sealed class ComposeUpCommand : AsyncCommand<ComposeUpCommand.Settings>
{
    public sealed class Settings : ComposeCommandSettings
    {
        [CommandOption("--pull")]
        [Description("Always pull images before starting.")]
        public bool Pull { get; set; }

        [CommandOption("-d|--detach")]
        [Description("Run containers in the background (default; foreground mode is not implemented yet).")]
        public bool Detach { get; set; } = true;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!ComposeFiles.TryResolve(settings, out var inputs, out var loadError))
        {
            AnsiConsole.MarkupLine(loadError);
            return 1;
        }

        if (inputs is null)
        {
            AnsiConsole.MarkupLine("[red]No compose file found.[/] Use [bold]-f <path>[/] or run from a directory containing compose.yaml / docker-compose.yml.");
            return 1;
        }

        var request = new UpRequest
        {
            ProjectName = settings.ProjectName ?? string.Empty,
            DefaultProjectName = inputs.DefaultProjectName,
            ComposeYaml = inputs.Yaml,
            Provider = settings.Provider ?? string.Empty,
            Pull = settings.Pull,
        };

        try
        {
            using var client = new WslccClient(settings.Host);
            var response = await AnsiConsole.Status()
                .StartAsync("Starting services...", async _ => await client.UpAsync(request, cancellationToken))
                .ConfigureAwait(false);

            AnsiConsole.MarkupLine($"[bold]Project:[/] {response.ProjectName.EscapeMarkup()}");

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
                    var id = RpcErrors.ShortId(result.ContainerId);
                    AnsiConsole.MarkupLine($"  [green]+[/] {result.Service.EscapeMarkup()} [grey]{id.EscapeMarkup()}[/] ({result.Status.EscapeMarkup()})");
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

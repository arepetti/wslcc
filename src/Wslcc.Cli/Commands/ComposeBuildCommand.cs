using System.ComponentModel;
using Grpc.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using Wslcc.Client;
using Wslcc.Grpc.Contracts;

namespace Wslcc.Cli.Commands;

/// <summary><c>wslcc compose build</c>: build the image for each service with a 'build:' section.</summary>
public sealed class ComposeBuildCommand : AsyncCommand<ComposeBuildCommand.Settings>
{
    public sealed class Settings : ComposeCommandSettings
    {
        [CommandArgument(0, "[SERVICES]")]
        [Description("Services to build. Defaults to every service with a 'build:' section.")]
        public string[] Services { get; set; } = Array.Empty<string>();
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!ComposeFiles.TryResolve(settings, out var inputs, out var loadError, settings.Services))
        {
            AnsiConsole.MarkupLine(loadError);
            return 1;
        }

        if (inputs is null)
        {
            AnsiConsole.MarkupLine("[red]No compose file found.[/] Use [bold]-f <path>[/] or run from a directory containing compose.yaml / docker-compose.yml.");
            return 1;
        }

        var request = new BuildRequest
        {
            ProjectName = settings.ProjectName ?? string.Empty,
            DefaultProjectName = inputs.DefaultProjectName,
            ComposeYaml = inputs.Yaml,
            Provider = settings.Provider ?? string.Empty,
            BaseDirectory = inputs.ProjectDirectory,
        };
        request.Services.AddRange(settings.Services);

        try
        {
            using var client = new WslccClient(settings.Host);
            var response = await AnsiConsole.Status()
                .StartAsync("Building images...", async _ => await client.BuildAsync(request, cancellationToken))
                .ConfigureAwait(false);

            AnsiConsole.MarkupLine($"[bold]Project:[/] {response.ProjectName.EscapeMarkup()}");

            if (response.Results.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No services to build.[/]");
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
                    AnsiConsole.MarkupLine($"  [green]+[/] {result.Service.EscapeMarkup()} ({result.Status.EscapeMarkup()})");
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

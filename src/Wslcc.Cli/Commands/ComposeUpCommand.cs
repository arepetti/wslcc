using System.ComponentModel;
using Grpc.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using Wslcc.Client;
using Wslcc.Grpc.Contracts;

namespace Wslcc.Cli.Commands;

/// <summary>
/// <c>wslcc compose up</c>: create and start the project's services. Attaches to their combined log
/// output by default (Ctrl+C gracefully stops the project); pass <c>-d</c>/<c>--detach</c> to return
/// immediately and leave the services running in the background.
/// </summary>
public sealed class ComposeUpCommand : AsyncCommand<ComposeUpCommand.Settings>
{
    public sealed class Settings : ComposeCommandSettings
    {
        [CommandOption("--pull")]
        [Description("Always pull images before starting.")]
        public bool Pull { get; set; }

        [CommandOption("--build")]
        [Description("Build images for services with a 'build:' section before starting, even if the image already exists.")]
        public bool Build { get; set; }

        [CommandOption("--no-build")]
        [Description("Do not build any images; fail if a service's image is missing.")]
        public bool NoBuild { get; set; }

        [CommandOption("-d|--detach")]
        [Description("Run containers in the background and return, instead of attaching to their log output.")]
        public bool Detach { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.Build && settings.NoBuild)
        {
            AnsiConsole.MarkupLine("[red]--build and --no-build cannot be used together.[/]");
            return 1;
        }

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
            BaseDirectory = inputs.ProjectDirectory,
            BuildPolicy = settings.Build ? BuildPolicy.Always
                : settings.NoBuild ? BuildPolicy.Never
                : BuildPolicy.Auto,
        };

        try
        {
            using var client = new WslccClient(settings.Host);
            var response = await AnsiConsole.Status()
                .StartAsync("Starting services...", async _ => await client.UpAsync(request, cancellationToken))
                .ConfigureAwait(false);

            AnsiConsole.MarkupLine($"[bold]Project:[/] {response.ProjectName.EscapeMarkup()}");

            var failed = PrintResults(response);
            if (failed > 0 || settings.Detach)
            {
                return failed > 0 ? 1 : 0;
            }

            return await AttachAsync(client, settings, inputs, cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            return RpcErrors.Report(ex);
        }
    }

    private static int PrintResults(UpResponse response)
    {
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

        return failed;
    }

    /// <summary>
    /// Foreground mode: follow the project's combined log output until the user interrupts (Ctrl+C), then
    /// gracefully stop the containers — mirroring an attached <c>docker compose up</c>. A second Ctrl+C
    /// abandons the wait for the stop to finish.
    /// </summary>
    private static async Task<int> AttachAsync(WslccClient client, Settings settings, ComposeInputs inputs, CancellationToken cancellationToken)
    {
        var logsRequest = new LogsRequest
        {
            ProjectName = settings.ProjectName ?? string.Empty,
            DefaultProjectName = inputs.DefaultProjectName,
            ComposeYaml = inputs.Yaml,
            Provider = settings.Provider ?? string.Empty,
            Follow = true,
        };

        AnsiConsole.MarkupLine("[grey]Attached to services. Press Ctrl+C to stop.[/]");

        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var interrupted = false;
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            // Intercept Ctrl+C so it stops the project gracefully instead of killing the CLI outright.
            e.Cancel = true;
            interrupted = true;
            streamCts.Cancel();
        };
        Console.CancelKeyPress += handler;

        try
        {
            await LogStreaming.RenderAsync(client, logsRequest, streamCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (streamCts.IsCancellationRequested)
        {
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && streamCts.IsCancellationRequested)
        {
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }

        // The stream ended on its own (e.g. every container exited); nothing left to stop.
        if (!interrupted)
        {
            return 0;
        }

        return await StopAsync(client, settings, inputs, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> StopAsync(WslccClient client, Settings settings, ComposeInputs inputs, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("[yellow]Gracefully stopping...[/] (press Ctrl+C again to force)");

        var stopRequest = new StopRequest
        {
            ProjectName = settings.ProjectName ?? string.Empty,
            DefaultProjectName = inputs.DefaultProjectName,
            ComposeYaml = inputs.Yaml,
            Provider = settings.Provider ?? string.Empty,
        };

        using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            stopCts.Cancel();
        };
        Console.CancelKeyPress += handler;

        try
        {
            var response = await client.StopAsync(stopRequest, stopCts.Token).ConfigureAwait(false);
            foreach (var result in response.Results)
            {
                if (string.Equals(result.Status, "failed", StringComparison.OrdinalIgnoreCase))
                {
                    AnsiConsole.MarkupLine($"  [red]x[/] {result.Service.EscapeMarkup()}: {result.Error.EscapeMarkup()}");
                }
                else
                {
                    AnsiConsole.MarkupLine($"  [green]-[/] {result.Service.EscapeMarkup()} ({result.Status.EscapeMarkup()})");
                }
            }

            // 130 = terminated by Ctrl+C (128 + SIGINT), matching docker compose up.
            return 130;
        }
        catch (Exception ex) when (stopCts.IsCancellationRequested
            && (ex is OperationCanceledException || (ex as RpcException)?.StatusCode == StatusCode.Cancelled))
        {
            AnsiConsole.MarkupLine("[yellow]Stop aborted; containers may still be running.[/] Use [bold]wslcc compose stop[/] or [bold]down[/].");
            return 130;
        }
        catch (RpcException ex)
        {
            return RpcErrors.Report(ex);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }
}

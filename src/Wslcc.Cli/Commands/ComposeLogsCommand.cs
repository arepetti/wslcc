using System.ComponentModel;
using Grpc.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using Wslcc.Client;
using Wslcc.Grpc.Contracts;

namespace Wslcc.Cli.Commands;

/// <summary><c>wslcc compose logs</c>: stream (or dump) log output from the project's containers.</summary>
public sealed class ComposeLogsCommand : AsyncCommand<ComposeLogsCommand.Settings>
{
    public sealed class Settings : ComposeCommandSettings
    {
        [CommandArgument(0, "[SERVICES]")]
        [Description("Services to show logs for. Defaults to every service with an existing container.")]
        public string[] Services { get; set; } = Array.Empty<string>();

        [CommandOption("--follow")]
        [Description("Keep streaming new log output (like 'tail -f'); stop with Ctrl+C. (Note: no '-f' short form; '-f' is already used for --file.)")]
        public bool Follow { get; set; }

        [CommandOption("--tail <N>")]
        [Description("Number of lines to show from the end of the logs. Defaults to all.")]
        public int? Tail { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var inputs = ComposeFiles.Resolve(settings.File);

        if (inputs is null && string.IsNullOrWhiteSpace(settings.ProjectName))
        {
            AnsiConsole.MarkupLine("[red]No compose file found and no --project-name given.[/]");
            return 1;
        }

        var request = new LogsRequest
        {
            ProjectName = settings.ProjectName ?? string.Empty,
            DefaultProjectName = inputs?.DefaultProjectName ?? string.Empty,
            ComposeYaml = inputs?.Yaml ?? string.Empty,
            Provider = settings.Provider ?? string.Empty,
            Follow = settings.Follow,
        };
        request.Services.AddRange(settings.Services);
        if (settings.Tail is { } tail)
        {
            request.Tail = tail;
        }

        // Ctrl+C should stop following gracefully (close the stream) rather than kill the process.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += handler;

        try
        {
            using var client = new WslccClient(settings.Host);
            var any = false;

            await foreach (var line in client.GetLogsAsync(request, cts.Token).ConfigureAwait(false))
            {
                any = true;
                AnsiConsole.MarkupLine($"[grey]{line.Service.EscapeMarkup()}[/] | {line.Line.EscapeMarkup()}");
            }

            if (!any)
            {
                AnsiConsole.MarkupLine("[grey]No log output.[/]");
            }

            return 0;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return 0;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && cts.IsCancellationRequested)
        {
            return 0;
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

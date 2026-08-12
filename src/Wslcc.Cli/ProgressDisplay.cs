using Spectre.Console;
using Wslcc.Grpc.Contracts;

namespace Wslcc.Cli;

/// <summary>Renders streamed <see cref="ServiceProgress"/> events to the console.</summary>
internal static class ProgressDisplay
{
    public static IProgress<ServiceProgress> Create() => new ProgressReporter();

    private sealed class ProgressReporter : IProgress<ServiceProgress>
    {
        public void Report(ServiceProgress value)
        {
            var service = string.IsNullOrEmpty(value.Service) ? "(project)" : value.Service;
            var phase = string.IsNullOrEmpty(value.Phase) ? "working" : value.Phase;

            if (string.Equals(value.Status, "in_progress", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine($"  [grey]{service.EscapeMarkup()}[/] {phase.EscapeMarkup()}...");
                return;
            }

            if (string.Equals(value.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                var detail = string.IsNullOrEmpty(value.Message) ? value.Status : value.Message;
                AnsiConsole.MarkupLine($"  [red]x[/] {service.EscapeMarkup()}: {detail.EscapeMarkup()}");
                return;
            }

            AnsiConsole.MarkupLine(
                $"  [green]+[/] {service.EscapeMarkup()} ({value.Status.EscapeMarkup()})");
        }
    }
}

using Spectre.Console;
using Spectre.Console.Cli;

namespace Wslcc.Cli.Commands;

/// <summary>
/// Top-level <c>wslcc version</c>: reports the wslcc build, the daemon, and every provider's
/// underlying tool version (analogous to <c>docker version</c>).
/// </summary>
public sealed class VersionCommand : AsyncCommand<HostSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, HostSettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine($"[bold]wslcc[/] (CLI) {CliVersion.Value.EscapeMarkup()}");

        var response = await DaemonClientHelper.TryGetVersionAsync(settings.Host, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            AnsiConsole.MarkupLine("[yellow]Daemon not reachable.[/] Start it with [bold]wslcc daemon start[/].");
            return 1;
        }

        var table = new Table().RoundedBorder();
        table.AddColumn("Component");
        table.AddColumn("Version");
        table.AddColumn("Status");

        table.AddRow(
            "wslccd (daemon)".EscapeMarkup(),
            response.DaemonVersion.EscapeMarkup(),
            "[green]running[/]");

        foreach (var provider in response.Providers)
        {
            var version = string.IsNullOrEmpty(provider.Version) ? "-" : provider.Version;
            var status = provider.Available
                ? "[green]available[/]"
                : "[yellow]unavailable[/]";

            table.AddRow(
                $"{provider.DisplayName} ({provider.Name})".EscapeMarkup(),
                version.EscapeMarkup(),
                status);
        }

        AnsiConsole.Write(table);

        foreach (var provider in response.Providers.Where(p => !p.Available && !string.IsNullOrEmpty(p.Details)))
        {
            AnsiConsole.MarkupLine($"[grey]{provider.Name.EscapeMarkup()}: {provider.Details.EscapeMarkup()}[/]");
        }

        return 0;
    }
}

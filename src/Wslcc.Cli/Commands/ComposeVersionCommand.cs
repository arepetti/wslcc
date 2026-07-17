using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Wslcc.Grpc.Contracts;

namespace Wslcc.Cli.Commands;

/// <summary>
/// <c>wslcc compose version</c>: reports the active provider/compose engine version, mirroring
/// <c>docker compose version</c> (supports <c>--short</c> and <c>--format</c>).
/// </summary>
public sealed class ComposeVersionCommand : AsyncCommand<ComposeVersionCommand.Settings>
{
    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--short")]
        [Description("Print only the version string.")]
        public bool Short { get; set; }

        [CommandOption("--format <FORMAT>")]
        [Description("Output format: 'pretty' (default) or 'json'.")]
        public string Format { get; set; } = "pretty";
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var response = await DaemonClientHelper.TryGetVersionAsync(settings.Host, settings.Provider, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (response is null)
        {
            AnsiConsole.MarkupLine("[yellow]Daemon not reachable.[/] Start it with [bold]wslcc daemon start[/].");
            return 1;
        }

        var provider = SelectProvider(response, settings.Provider);
        if (provider is null)
        {
            AnsiConsole.MarkupLine("[red]No provider is available.[/]");
            return 1;
        }

        var version = string.IsNullOrEmpty(provider.Version) ? "unknown" : provider.Version;

        if (settings.Short)
        {
            AnsiConsole.WriteLine(version);
            return 0;
        }

        if (string.Equals(settings.Format, "json", StringComparison.OrdinalIgnoreCase))
        {
            var json = JsonSerializer.Serialize(
                new
                {
                    provider = provider.Name,
                    displayName = provider.DisplayName,
                    version,
                    available = provider.Available,
                },
                new JsonSerializerOptions { WriteIndented = true });
            AnsiConsole.WriteLine(json);
            return 0;
        }

        AnsiConsole.MarkupLine($"[bold]{provider.DisplayName.EscapeMarkup()}[/] version [green]{version.EscapeMarkup()}[/] (provider: {provider.Name.EscapeMarkup()})");
        if (!provider.Available && !string.IsNullOrEmpty(provider.Details))
        {
            AnsiConsole.MarkupLine($"[grey]{provider.Details.EscapeMarkup()}[/]");
        }

        return 0;
    }

    private static ComponentVersion? SelectProvider(GetVersionResponse response, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return response.Providers.FirstOrDefault(
                p => string.Equals(p.Name, requested, StringComparison.OrdinalIgnoreCase));
        }

        return response.Providers.FirstOrDefault(p => p.Available) ?? response.Providers.FirstOrDefault();
    }
}

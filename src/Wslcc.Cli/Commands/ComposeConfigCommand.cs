using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Wslcc.Compose;

namespace Wslcc.Cli.Commands;

/// <summary>
/// <c>wslcc compose config</c>: parse, merge, interpolate and profile-filter the compose file(s) and render
/// the fully-resolved document. This runs entirely client-side (via <see cref="ComposeLoader"/>) and does
/// not require a running daemon — the output is exactly the document the other verbs send to <c>wslccd</c>
/// (with the effective project name added as <c>name:</c>).
/// </summary>
public sealed class ComposeConfigCommand : Command<ComposeConfigCommand.Settings>
{
    public sealed class Settings : ComposeCommandSettings
    {
        [CommandOption("--services")]
        [Description("Print the enabled service names instead of the full config.")]
        public bool Services { get; set; }

        [CommandOption("--volumes")]
        [Description("Print the declared volume names instead of the full config.")]
        public bool Volumes { get; set; }

        [CommandOption("--images")]
        [Description("Print the service image references instead of the full config.")]
        public bool Images { get; set; }

        [CommandOption("--profiles")]
        [Description("Print the profile names declared across services instead of the full config.")]
        public bool Profiles { get; set; }

        [CommandOption("--hash <SERVICES>")]
        [Description("Print a config hash per service. Use '*' for all services or a comma-separated list.")]
        public string? Hash { get; set; }

        [CommandOption("--format <FORMAT>")]
        [Description("Output format for the full config: 'yaml' (default) or 'json'.")]
        public string Format { get; set; } = "yaml";

        [CommandOption("--no-interpolate")]
        [Description("Do not interpolate ${VAR} references; leave them verbatim.")]
        public bool NoInterpolate { get; set; }

        [CommandOption("-q|--quiet")]
        [Description("Only validate the configuration; print nothing on success.")]
        public bool Quiet { get; set; }

        [CommandOption("-o|--output <PATH>")]
        [Description("Write the resolved config to a file instead of stdout.")]
        public string? Output { get; set; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        bool asJson;
        switch (settings.Format.Trim().ToLowerInvariant())
        {
            case "yaml" or "yml":
                asJson = false;
                break;
            case "json":
                asJson = true;
                break;
            default:
                AnsiConsole.MarkupLine($"[red]Unknown --format '{settings.Format.EscapeMarkup()}'.[/] Use 'yaml' or 'json'.");
                return 1;
        }

        var reportModes = (settings.Services ? 1 : 0) + (settings.Volumes ? 1 : 0) + (settings.Images ? 1 : 0)
            + (settings.Profiles ? 1 : 0) + (settings.Hash is not null ? 1 : 0);
        if (reportModes > 1)
        {
            AnsiConsole.MarkupLine("[red]Only one of --services, --volumes, --images, --profiles or --hash may be used at a time.[/]");
            return 1;
        }

        // Warnings go to stderr so a piped/redirected config document stays clean.
        if (!ComposeFiles.TryResolve(settings, out var inputs, out var loadError, printWarnings: false, interpolate: !settings.NoInterpolate))
        {
            AnsiConsole.MarkupLine(loadError);
            return 1;
        }

        if (inputs is null)
        {
            AnsiConsole.MarkupLine("[red]No compose file found.[/] Use [bold]-f <path>[/] or run from a directory containing compose.yaml / docker-compose.yml.");
            return 1;
        }

        foreach (var warning in inputs.Warnings)
        {
            Console.Error.WriteLine($"warning: {warning}");
        }

        // --quiet: resolution above already validated the document; emit nothing.
        if (settings.Quiet)
        {
            return 0;
        }

        if (settings.Hash is not null)
        {
            return PrintHashes(inputs.Yaml, settings.Hash);
        }

        if (settings.Services || settings.Volumes || settings.Images || settings.Profiles)
        {
            var names =
                settings.Services ? ComposeConfigView.ServiceNames(inputs.Yaml) :
                settings.Volumes ? ComposeConfigView.VolumeNames(inputs.Yaml) :
                settings.Images ? ComposeConfigView.ImageNames(inputs.Yaml) :
                inputs.DeclaredProfiles;

            foreach (var name in names)
            {
                Console.Out.WriteLine(name);
            }

            return 0;
        }

        var projectName = ComposeConfigView.ResolveProjectName(inputs.Yaml, settings.ProjectName, inputs.DefaultProjectName);
        var document = ComposeConfigView.Render(inputs.Yaml, projectName, asJson);

        if (!string.IsNullOrWhiteSpace(settings.Output))
        {
            var path = Path.GetFullPath(settings.Output);
            File.WriteAllText(path, document);
            AnsiConsole.MarkupLine($"[green]Wrote[/] {path.EscapeMarkup()}");
            return 0;
        }

        Console.Out.Write(document);
        if (!document.EndsWith('\n'))
        {
            Console.Out.WriteLine();
        }

        return 0;
    }

    private static int PrintHashes(string resolvedYaml, string pattern)
    {
        var hashes = ComposeHash.ComputeServiceHashes(resolvedYaml);

        IEnumerable<string> selected = pattern.Trim() == "*"
            ? hashes.Keys.OrderBy(name => name, StringComparer.Ordinal)
            : pattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var service in selected)
        {
            if (!hashes.TryGetValue(service, out var hash))
            {
                AnsiConsole.MarkupLine($"[red]No such service:[/] {service.EscapeMarkup()}");
                return 1;
            }

            Console.Out.WriteLine($"{service} {hash}");
        }

        return 0;
    }
}

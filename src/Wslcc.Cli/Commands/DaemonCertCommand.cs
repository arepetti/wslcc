using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Wslcc.Cli.Commands;

/// <summary>
/// <c>wslcc daemon cert</c>: writes a self-signed PEM certificate for the optional HTTPS endpoint.
/// Does not enable HTTP or create a bearer token.
/// </summary>
public sealed class DaemonCertCommand : Command<DaemonCertCommand.Settings>
{
    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--hostname <NAME>")]
        [Description("DNS name or IP for the certificate SAN. Repeatable. Defaults to localhost and 127.0.0.1.")]
        public string[] Hostname { get; set; } = Array.Empty<string>();

        [CommandOption("--out <DIR>")]
        [Description("Directory for server.pem and server.key. Defaults to %LOCALAPPDATA%\\wslcc\\certs.")]
        public string? Out { get; set; }

        [CommandOption("--days <N>")]
        [Description("Certificate lifetime in days (default 365).")]
        public int Days { get; set; } = 365;

        [CommandOption("--force")]
        [Description("Overwrite an existing certificate and key in the output directory.")]
        public bool Force { get; set; }

        [CommandOption("--write-config")]
        [Description("Set CertificatePath / CertificateKeyPath in the daemon appsettings.json when that file is writable.")]
        public bool WriteConfig { get; set; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        DaemonCertificateResult result;
        try
        {
            result = DaemonCertificateWriter.Write(new DaemonCertificateRequest
            {
                Hostnames = settings.Hostname,
                OutputDirectory = settings.Out,
                Days = settings.Days,
                Force = settings.Force,
            });
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]Wrote certificate[/]");
        AnsiConsole.MarkupLine($"  cert: {result.CertificatePath.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"  key:  {result.KeyPath.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"  sha256: {result.Sha256Fingerprint.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"  SAN: {string.Join(", ", result.Hostnames).EscapeMarkup()}");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Clients must trust this certificate (it is not in the system store):");
        AnsiConsole.MarkupLine($"  [bold]--tls-ca {result.CertificatePath.EscapeMarkup()}[/]  (or [bold]--wslcc-tls-ca[/] on compose commands)");
        AnsiConsole.MarkupLine("  or set [bold]WSLCC_TLS_CA[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("This command does [bold]not[/] enable HTTP or create a bearer token. See [bold]docs/daemon.md[/].");

        if (settings.WriteConfig)
            TryWriteConfig(result);
        else
            PrintConfigSnippet(result);

        return 0;
    }

    private static void TryWriteConfig(DaemonCertificateResult result)
    {
        var daemonPath = DaemonLocator.Find();
        if (daemonPath is null)
        {
            AnsiConsole.MarkupLine("[yellow]Could not locate wslccd; config was not updated.[/]");
            PrintConfigSnippet(result);
            return;
        }

        var settingsPath = Path.Combine(Path.GetDirectoryName(daemonPath) ?? ".", "appsettings.json");
        if (!File.Exists(settingsPath))
        {
            AnsiConsole.MarkupLine($"[yellow]No appsettings.json next to wslccd ({settingsPath.EscapeMarkup()}).[/]");
            PrintConfigSnippet(result);
            return;
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            var updated = DaemonCertificateWriter.PatchAppsettings(json, result.CertificatePath, result.KeyPath);
            File.WriteAllText(settingsPath, updated);
            AnsiConsole.MarkupLine($"[green]Updated[/] {settingsPath.EscapeMarkup()} (CertificatePath / CertificateKeyPath only).");
            AnsiConsole.MarkupLine("HTTP is still disabled until you set [bold]Http:Enabled[/] and a token.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            AnsiConsole.MarkupLine($"[yellow]Could not write {settingsPath.EscapeMarkup()}[/] ({ex.Message.EscapeMarkup()}).");
            PrintConfigSnippet(result);
        }
    }

    private static void PrintConfigSnippet(DaemonCertificateResult result)
    {
        AnsiConsole.MarkupLine("Set these in the daemon appsettings.json [bold]Wslcc:Http[/] section:");
        AnsiConsole.WriteLine($"    \"CertificatePath\": \"{result.CertificatePath.Replace("\\", "\\\\")}\",");
        AnsiConsole.WriteLine($"    \"CertificateKeyPath\": \"{result.KeyPath.Replace("\\", "\\\\")}\"");
    }
}

using System.Text;
using Wslcc.Abstractions;

namespace Wslcc.Providers.Common;

/// <summary>
/// Builds argument strings for the standard container CLIs (docker, wslc, ...). Kept separate and
/// pure so it can be unit-tested without invoking a process.
/// </summary>
public static class CliCommandBuilder
{
    /// <summary>Field separator used in the <c>ps --format</c> template (ASCII unit separator).</summary>
    public const char FieldSeparator = '\u001f';

    /// <summary>Go-template used for <c>ps</c> output. Fields: id, names, image, state, status, ports, labels.</summary>
    public static readonly string PsFormat = string.Join(
        FieldSeparator,
        "{{.ID}}", "{{.Names}}", "{{.Image}}", "{{.State}}", "{{.Status}}", "{{.Ports}}", "{{.Labels}}");

    public static string BuildRunArguments(ContainerRunSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Image))
        {
            throw new ProviderException($"Service '{spec.Name}' has no image.");
        }

        var args = new List<string> { "run" };

        if (spec.Detach)
        {
            args.Add("-d");
        }

        if (!string.IsNullOrEmpty(spec.Name))
        {
            args.Add("--name");
            args.Add(spec.Name);
        }

        foreach (var label in spec.Labels)
        {
            args.Add("--label");
            args.Add($"{label.Key}={label.Value}");
        }

        foreach (var env in spec.Environment)
        {
            args.Add("-e");
            args.Add(env.Value is null ? env.Key : $"{env.Key}={env.Value}");
        }

        foreach (var port in spec.Ports)
        {
            args.Add("-p");
            args.Add(port);
        }

        if (!string.IsNullOrWhiteSpace(spec.Restart))
        {
            args.Add("--restart");
            args.Add(spec.Restart!);
        }

        args.Add(spec.Image);

        foreach (var token in spec.Command)
        {
            args.Add(token);
        }

        return Join(args);
    }

    public static string BuildPsArguments(string? projectName, bool all)
    {
        var args = new List<string> { "ps" };

        if (all)
        {
            args.Add("--all");
        }

        args.Add("--filter");
        args.Add(projectName is null
            ? $"label={WslccLabels.Project}"
            : $"label={WslccLabels.Project}={projectName}");

        args.Add("--format");
        args.Add(PsFormat);

        return Join(args);
    }

    public static string BuildPullArguments(string image) => Join(new[] { "pull", image });

    public static string BuildImageInspectArguments(string image) => Join(new[] { "image", "inspect", image });

    public static string BuildStopArguments(string container) => Join(new[] { "stop", container });

    public static string BuildRemoveArguments(string container, bool force)
        => force ? Join(new[] { "rm", "-f", container }) : Join(new[] { "rm", container });

    internal static string Join(IEnumerable<string> args)
        => string.Join(" ", args.Select(Quote));

    internal static string Quote(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        var needsQuotes = value.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0;
        if (!needsQuotes)
        {
            return value;
        }

        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            if (c == '"')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        sb.Append('"');
        return sb.ToString();
    }
}

using System.Globalization;
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

    public static string BuildBuildArguments(ImageBuildSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Context))
        {
            throw new ProviderException("No build context specified.");
        }

        var args = new List<string> { "build" };

        if (!string.IsNullOrWhiteSpace(spec.Tag))
        {
            args.Add("-t");
            args.Add(spec.Tag);
        }

        if (!string.IsNullOrWhiteSpace(spec.Dockerfile))
        {
            args.Add("-f");
            args.Add(spec.Dockerfile!);
        }

        if (!string.IsNullOrWhiteSpace(spec.Target))
        {
            args.Add("--target");
            args.Add(spec.Target!);
        }

        foreach (var arg in spec.Args)
        {
            args.Add("--build-arg");
            args.Add(arg.Value is null ? arg.Key : $"{arg.Key}={arg.Value}");
        }

        args.Add(spec.Context);

        return Join(args);
    }

    public static string BuildImageInspectArguments(string image) => Join(new[] { "image", "inspect", image });

    public static string BuildStopArguments(string container) => Join(new[] { "stop", container });

    public static string BuildStartArguments(string container) => Join(new[] { "start", container });

    public static string BuildRestartArguments(string container) => Join(new[] { "restart", container });

    public static string BuildRemoveArguments(string container, bool force)
        => force ? Join(new[] { "rm", "-f", container }) : Join(new[] { "rm", container });

    public static string BuildLogsArguments(string container, bool follow, int? tail)
    {
        var args = new List<string> { "logs" };

        if (follow)
        {
            args.Add("--follow");
        }

        if (tail is { } n)
        {
            args.Add("--tail");
            args.Add(n.ToString(CultureInfo.InvariantCulture));
        }

        args.Add(container);

        return Join(args);
    }

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

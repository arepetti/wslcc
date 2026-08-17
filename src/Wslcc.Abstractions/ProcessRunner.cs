using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace Wslcc.Abstractions;

/// <summary>Result of running an external process.</summary>
/// <param name="ExitCode">The process's exit code.</param>
/// <param name="StandardOutput">Everything the process wrote to stdout.</param>
/// <param name="StandardError">Everything the process wrote to stderr.</param>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>Whether the process exited with code 0.</summary>
    public bool Success => ExitCode == 0;
}

/// <summary>
/// Minimal helper for invoking external CLIs (e.g. <c>wslc</c>, <c>docker</c>) and capturing output.
/// </summary>
public static class ProcessRunner
{
    /// <summary>
    /// Runs a process and returns its result, or <c>null</c> if the executable could not be started
    /// (for example, it is not installed / not on PATH).
    /// </summary>
    /// <param name="fileName">Executable to run; resolved through PATH when not a full path.</param>
    /// <param name="arguments">Already-quoted argument string.</param>
    /// <param name="cancellationToken">Cancels the wait for the process to exit.</param>
    /// <returns>The exit code and captured output, or <c>null</c> when the executable could not be launched.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="arguments"/> is <c>null</c>.</exception>
    public static async Task<ProcessResult?> TryRunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };

        try
        {
            if (!process.Start())
    return null;
        }
        catch (Exception)
        {
            // Executable missing or not launchable.
            return null;
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var stdoutTask = ReadStreamAsync(process.StandardOutput, stdout);
        var stderrTask = ReadStreamAsync(process.StandardError, stderr);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static async Task ReadStreamAsync(System.IO.StreamReader reader, StringBuilder target)
    {
        var text = await reader.ReadToEndAsync().ConfigureAwait(false);
        target.Append(text);
    }

    /// <summary>
    /// Starts a process and yields its combined stdout/stderr, line by line, as they are written
    /// (e.g. for <c>docker logs --follow</c>). Cancelling <paramref name="cancellationToken"/> kills the
    /// process (and its child tree) so a "follow" invocation stops promptly. Yields nothing if the
    /// executable could not be started.
    /// </summary>
    /// <param name="fileName">Executable to run; resolved through PATH when not a full path.</param>
    /// <param name="arguments">Already-quoted argument string.</param>
    /// <param name="cancellationToken">Kills the process (and its children) when cancelled, ending the sequence.</param>
    /// <returns>The process's output lines, in the order they were written.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="arguments"/> is <c>null</c>.</exception>
    public static IAsyncEnumerable<string> StreamLinesAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        return StreamLinesCoreAsync(fileName, arguments, cancellationToken);
    }

    private static async IAsyncEnumerable<string> StreamLinesCoreAsync(
        string fileName,
        string arguments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
    channel.Writer.TryWrite(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
    channel.Writer.TryWrite(e.Data);
        };

        bool started;
        try
        {
            started = process.Start();
        }
        catch (Exception)
        {
            started = false;
        }

        if (!started)
    yield break;

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process already exited between the check and the kill; ignore.
            }
        });

        // Cancellation is handled via the kill above (which flushes remaining buffered output and
        // completes the channel below), not by tearing down the enumeration itself.
        var exitTask = process.WaitForExitAsync(CancellationToken.None)
            .ContinueWith(_ => channel.Writer.TryComplete(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        await foreach (var line in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            yield return line;
        }

        await exitTask.ConfigureAwait(false);
    }
}

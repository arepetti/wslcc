using System.Diagnostics;
using System.Text;

namespace Wslcc.Abstractions;

/// <summary>Result of running an external process.</summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
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
    public static async Task<ProcessResult?> TryRunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default)
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

        using var process = new Process { StartInfo = psi };

        try
        {
            if (!process.Start())
            {
                return null;
            }
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

#if NET5_0_OR_GREATER
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
#else
        await Task.Run(() => process.WaitForExit(), cancellationToken).ConfigureAwait(false);
#endif
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static async Task ReadStreamAsync(System.IO.StreamReader reader, StringBuilder target)
    {
        var text = await reader.ReadToEndAsync().ConfigureAwait(false);
        target.Append(text);
    }
}

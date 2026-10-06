using System.Diagnostics;

namespace ErgoProxy.Core.Platform;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName, 
        IEnumerable<string> arguments, 
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(10);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(effectiveTimeout);

        try
        {
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);

            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            return new ProcessResult(proc.ExitCode, stdout.Trim(), stderr.Trim());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult(-1, string.Empty, $"Process '{fileName}' timed out after {effectiveTimeout.TotalSeconds}s.");
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, string.Empty, $"Execution failed for '{fileName}': {ex.Message}");
        }
    }
}

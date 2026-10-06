using System.Diagnostics;
using ErgoProxy.Core.Platform;
using ErgoProxy.Core.Tunnel.Native;

namespace ErgoProxy.Core.Daemon;

public static class DaemonLauncher
{
    public static async Task<(bool Success, string Message)> EnsureRunningAsync(
        DaemonClient client,
        CancellationToken ct = default)
    {
        if (await client.IsRunningAsync(ct).ConfigureAwait(false))
        {
            return (true, "Daemon is already running.");
        }

        if (!LinuxNative.IsLinux)
        {
            return (false, "Device-wide network tunnelling is only supported on Linux in Phase 1.");
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
        {
            return (false, "Unable to determine application executable path.");
        }

        var uid = LinuxNative.GetUid();

        // If we are already root, spawn detached daemon directly
        if (LinuxNative.IsRoot)
        {
            StartDetachedProcess(processPath, ["daemon", "--detach", "--uid", uid.ToString()]);
        }
        else
        {
            // Prompt for sudo credentials interactively if needed
            var sudoCheck = await ProcessRunner.RunAsync("sudo", ["-n", "true"], TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            if (sudoCheck.ExitCode != 0)
            {
                // Run sudo -v with console attached
                var sudoValidate = Process.Start(new ProcessStartInfo("sudo", "-v") { UseShellExecute = false });
                if (sudoValidate != null)
                {
                    await sudoValidate.WaitForExitAsync(ct).ConfigureAwait(false);
                    if (sudoValidate.ExitCode != 0)
                    {
                        return (false, "Root privileges (sudo) are required to establish a device-wide network tunnel.");
                    }
                }
            }

            // Launch detached daemon using sudo
            StartDetachedProcess("sudo", [processPath, "daemon", "--detach", "--uid", uid.ToString()]);
        }

        // Wait for socket to become available and responding
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(6))
        {
            if (await client.IsRunningAsync(ct).ConfigureAwait(false))
            {
                return (true, "Daemon started successfully.");
            }
            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        return (false, "Timed out waiting for ErgoProxy background daemon to start.");
    }

    private static void StartDetachedProcess(string fileName, string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        var proc = Process.Start(psi);
        if (proc != null)
        {
            // Allow child process to run detached
            proc.StandardInput.Close();
        }
    }
}

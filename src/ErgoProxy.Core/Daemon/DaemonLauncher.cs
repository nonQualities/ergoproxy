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

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
        {
            return (false, "Device-wide network tunnelling is only supported on Linux and Windows.");
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
        {
            return (false, "Unable to determine application executable path.");
        }

        if (OperatingSystem.IsWindows())
        {
            if (IsWindowsAdministrator())
            {
                StartDetachedProcess(processPath, ["daemon", "--detach"]);
            }
            else
            {
                // Trigger UAC prompt for elevation
                var psi = new ProcessStartInfo(processPath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                psi.ArgumentList.Add("daemon");
                psi.ArgumentList.Add("--detach");

                try
                {
                    var proc = Process.Start(psi);
                    if (proc == null)
                    {
                        return (false, "Failed to launch elevated ErgoProxy daemon.");
                    }
                }
                catch (Exception ex)
                {
                    return (false, $"Administrator privileges are required to configure network routes and Wintun: {ex.Message}");
                }
            }
        }
        else if (OperatingSystem.IsLinux())
        {
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
        }

        // Wait for daemon to become available and responding
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(8))
        {
            if (await client.IsRunningAsync(ct).ConfigureAwait(false))
            {
                return (true, "Daemon started successfully.");
            }
            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        return (false, "Timed out waiting for ErgoProxy background daemon to start.");
    }

    private static bool IsWindowsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(id);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
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
            proc.StandardInput.Close();
        }
    }
}

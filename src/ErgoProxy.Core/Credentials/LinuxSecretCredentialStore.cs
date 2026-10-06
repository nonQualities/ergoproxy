using System.Diagnostics;
using System.Runtime.InteropServices;
using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Credentials;

public sealed class LinuxSecretCredentialStore : ICredentialStore
{
    private readonly ICredentialStore _fallbackStore;
    private readonly bool _hasSecretTool;

    public string StoreName => _hasSecretTool ? "LinuxSecretService" : _fallbackStore.StoreName;

    public LinuxSecretCredentialStore(ICredentialStore? fallbackStore = null)
    {
        _fallbackStore = fallbackStore ?? new EncryptedFileCredentialStore();
        _hasSecretTool = CheckSecretToolAvailable();
    }

    private static bool CheckSecretToolAvailable()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return false;

        try
        {
            var psi = new ProcessStartInfo("which", "secret-tool")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(1000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task SaveCredentialsAsync(string credentialReference, ProxyCredentials credentials, CancellationToken ct = default)
    {
        if (_hasSecretTool)
        {
            try
            {
                // secret-tool store --label="ErgoProxy Credential" service ergoproxy reference <ref> username <user>
                var psi = new ProcessStartInfo("secret-tool",
                    $"store --label=\"ErgoProxy {credentialReference}\" service ergoproxy reference {credentialReference} username {credentials.Username}")
                {
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.StandardInput.WriteAsync(credentials.Password);
                    proc.StandardInput.Close();
                    await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                    if (proc.ExitCode == 0) return;
                }
            }
            catch
            {
                // fallback to encrypted store
            }
        }

        await _fallbackStore.SaveCredentialsAsync(credentialReference, credentials, ct).ConfigureAwait(false);
    }

    public async Task<ProxyCredentials?> GetCredentialsAsync(string credentialReference, CancellationToken ct = default)
    {
        if (_hasSecretTool)
        {
            try
            {
                var psi = new ProcessStartInfo("secret-tool",
                    $"lookup service ergoproxy reference {credentialReference}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var password = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
                    await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                    if (proc.ExitCode == 0 && !string.IsNullOrEmpty(password))
                    {
                        // Secret service stores password; username is stored in fallback or reference
                        var fallbackCreds = await _fallbackStore.GetCredentialsAsync(credentialReference, ct).ConfigureAwait(false);
                        return new ProxyCredentials(fallbackCreds?.Username ?? credentialReference, password.TrimEnd('\r', '\n'));
                    }
                }
            }
            catch
            {
                // fallback
            }
        }

        return await _fallbackStore.GetCredentialsAsync(credentialReference, ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteCredentialsAsync(string credentialReference, CancellationToken ct = default)
    {
        var removed = false;
        if (_hasSecretTool)
        {
            try
            {
                var psi = new ProcessStartInfo("secret-tool",
                    $"clear service ergoproxy reference {credentialReference}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                    removed = proc.ExitCode == 0;
                }
            }
            catch
            {
                // ignore
            }
        }

        var fallbackRemoved = await _fallbackStore.DeleteCredentialsAsync(credentialReference, ct).ConfigureAwait(false);
        return removed || fallbackRemoved;
    }

    public async Task<bool> ExistsAsync(string credentialReference, CancellationToken ct = default)
    {
        var creds = await GetCredentialsAsync(credentialReference, ct).ConfigureAwait(false);
        return creds != null;
    }
}

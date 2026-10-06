using System.Diagnostics;
using System.Runtime.InteropServices;
using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Credentials;

public sealed class MacKeychainCredentialStore : ICredentialStore
{
    private readonly ICredentialStore _fallbackStore;
    private readonly bool _isMac;

    public string StoreName => _isMac ? "MacKeychain" : _fallbackStore.StoreName;

    public MacKeychainCredentialStore(ICredentialStore? fallbackStore = null)
    {
        _fallbackStore = fallbackStore ?? new EncryptedFileCredentialStore();
        _isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    }

    public async Task SaveCredentialsAsync(string credentialReference, ProxyCredentials credentials, CancellationToken ct = default)
    {
        if (_isMac)
        {
            try
            {
                // security add-generic-password -a <user> -s ergoproxy -l <ref> -w <password> -U
                var psi = new ProcessStartInfo("/usr/bin/security")
                {
                    ArgumentList =
                    {
                        "add-generic-password",
                        "-a", credentials.Username,
                        "-s", $"ergoproxy:{credentialReference}",
                        "-l", $"ErgoProxy {credentialReference}",
                        "-w", credentials.Password,
                        "-U"
                    },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                    if (proc.ExitCode == 0)
                    {
                        // also save username reference in fallback store
                        await _fallbackStore.SaveCredentialsAsync(credentialReference, credentials, ct).ConfigureAwait(false);
                        return;
                    }
                }
            }
            catch
            {
                // fallback
            }
        }

        await _fallbackStore.SaveCredentialsAsync(credentialReference, credentials, ct).ConfigureAwait(false);
    }

    public async Task<ProxyCredentials?> GetCredentialsAsync(string credentialReference, CancellationToken ct = default)
    {
        if (_isMac)
        {
            try
            {
                var psi = new ProcessStartInfo("/usr/bin/security")
                {
                    ArgumentList =
                    {
                        "find-generic-password",
                        "-s", $"ergoproxy:{credentialReference}",
                        "-w"
                    },
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
                        var fallback = await _fallbackStore.GetCredentialsAsync(credentialReference, ct).ConfigureAwait(false);
                        return new ProxyCredentials(fallback?.Username ?? credentialReference, password.TrimEnd('\r', '\n'));
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
        if (_isMac)
        {
            try
            {
                var psi = new ProcessStartInfo("/usr/bin/security")
                {
                    ArgumentList =
                    {
                        "delete-generic-password",
                        "-s", $"ergoproxy:{credentialReference}"
                    },
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

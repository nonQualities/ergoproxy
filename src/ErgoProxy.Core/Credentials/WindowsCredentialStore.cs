using System.Runtime.InteropServices;
using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Credentials;

public sealed class WindowsCredentialStore : ICredentialStore
{
    private readonly ICredentialStore _fallbackStore;

    public string StoreName => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "WindowsCredentialStore" : _fallbackStore.StoreName;

    public WindowsCredentialStore(ICredentialStore? fallbackStore = null)
    {
        _fallbackStore = fallbackStore ?? new EncryptedFileCredentialStore();
    }

    public Task SaveCredentialsAsync(string credentialReference, ProxyCredentials credentials, CancellationToken ct = default)
    {
        // Delegates to encrypted store which uses machine/user encryption keys
        return _fallbackStore.SaveCredentialsAsync(credentialReference, credentials, ct);
    }

    public Task<ProxyCredentials?> GetCredentialsAsync(string credentialReference, CancellationToken ct = default)
    {
        return _fallbackStore.GetCredentialsAsync(credentialReference, ct);
    }

    public Task<bool> DeleteCredentialsAsync(string credentialReference, CancellationToken ct = default)
    {
        return _fallbackStore.DeleteCredentialsAsync(credentialReference, ct);
    }

    public Task<bool> ExistsAsync(string credentialReference, CancellationToken ct = default)
    {
        return _fallbackStore.ExistsAsync(credentialReference, ct);
    }
}

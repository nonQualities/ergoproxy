using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Credentials;

public interface ICredentialStore
{
    string StoreName { get; }
    Task SaveCredentialsAsync(string credentialReference, ProxyCredentials credentials, CancellationToken ct = default);
    Task<ProxyCredentials?> GetCredentialsAsync(string credentialReference, CancellationToken ct = default);
    Task<bool> DeleteCredentialsAsync(string credentialReference, CancellationToken ct = default);
    Task<bool> ExistsAsync(string credentialReference, CancellationToken ct = default);
}

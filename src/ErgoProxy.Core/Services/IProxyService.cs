using ErgoProxy.Core.Models;
using ErgoProxy.Core.Network;

namespace ErgoProxy.Core.Services;

public sealed record TunnelOperationResult(bool Success, string Message, TunnelStatus? Status = null);

public sealed record ProxyStatusInfo(
    ProxyProfile? ActiveProfile,
    TunnelStatus TunnelStatus,
    ProxyTestResult? LastTestResult,
    DateTimeOffset? LastTestedAt,
    bool HasPendingRecovery,
    string? RecoveryMessage);

public interface IProxyService
{
    Task<IReadOnlyList<ProxyProfile>> GetProfilesAsync(CancellationToken ct = default);
    Task<ProxyProfile?> GetProfileAsync(string id, CancellationToken ct = default);
    Task<ProxyProfile> SaveProfileAsync(ProxyProfile profile, ProxyCredentials? credentials = null, CancellationToken ct = default);
    Task<bool> DeleteProfileAsync(string id, CancellationToken ct = default);
    Task SetActiveProfileAsync(string id, CancellationToken ct = default);
    Task<ProxyProfile?> GetActiveProfileAsync(CancellationToken ct = default);

    Task<ProxyTestResult> TestProfileAsync(string? profileId = null, ProxyTestOptions? options = null, CancellationToken ct = default);
    Task<TunnelOperationResult> ConnectAsync(string? profileId = null, CancellationToken ct = default);
    Task<TunnelOperationResult> DisconnectAsync(CancellationToken ct = default);
    Task<ProxyStatusInfo> GetStatusAsync(CancellationToken ct = default);

    Task SaveCredentialsAsync(string reference, ProxyCredentials credentials, CancellationToken ct = default);
    Task<ProxyCredentials?> GetCredentialsAsync(string reference, CancellationToken ct = default);
}

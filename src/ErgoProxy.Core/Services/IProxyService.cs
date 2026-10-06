using ErgoProxy.Core.Models;
using ErgoProxy.Core.Network;
using ErgoProxy.Core.Platform;

namespace ErgoProxy.Core.Services;

public sealed record ProxyStatusInfo(
    ProxyProfile? ActiveProfile,
    string? ConfiguredEndpoint,
    bool IsSystemProxyApplied,
    bool IsSystemProxyActuallyActive,
    ProxyTestResult? LastTestResult,
    DateTimeOffset? LastTestedAt,
    string PlatformName,
    bool PlatformSupported,
    bool HasConflict,
    string? ConflictMessage,
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
    Task<ProxyApplyResult> ConnectAsync(string? profileId = null, bool force = false, CancellationToken ct = default);
    Task<ProxyRestoreResult> DisconnectAsync(bool force = false, CancellationToken ct = default);
    Task<ProxyStatusInfo> GetStatusAsync(CancellationToken ct = default);

    Task SaveCredentialsAsync(string reference, ProxyCredentials credentials, CancellationToken ct = default);
    Task<ProxyCredentials?> GetCredentialsAsync(string reference, CancellationToken ct = default);
}

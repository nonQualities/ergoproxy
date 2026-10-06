using ErgoProxy.Core.Credentials;
using ErgoProxy.Core.Models;
using ErgoProxy.Core.Network;
using ErgoProxy.Core.Platform;
using ErgoProxy.Core.Storage;
using ErgoProxy.Core.Validation;

namespace ErgoProxy.Core.Services;

public sealed class ProxyService : IProxyService
{
    private readonly IProfileRepository _profileRepo;
    private readonly IStateManager _stateManager;
    private readonly ICredentialStore _credentialStore;
    private readonly IProxyTester _proxyTester;
    private readonly IPlatformAdapter _platformAdapter;
    private readonly IProfileValidator _validator;

    public ProxyService(
        IProfileRepository profileRepo,
        IStateManager stateManager,
        ICredentialStore credentialStore,
        IProxyTester proxyTester,
        IPlatformAdapter platformAdapter,
        IProfileValidator? validator = null)
    {
        _profileRepo = profileRepo;
        _stateManager = stateManager;
        _credentialStore = credentialStore;
        _proxyTester = proxyTester;
        _platformAdapter = platformAdapter;
        _validator = validator ?? new ProfileValidator();
    }

    public Task<IReadOnlyList<ProxyProfile>> GetProfilesAsync(CancellationToken ct = default)
    {
        return _profileRepo.GetAllAsync(ct);
    }

    public Task<ProxyProfile?> GetProfileAsync(string id, CancellationToken ct = default)
    {
        return _profileRepo.GetByIdAsync(id, ct);
    }

    public async Task<ProxyProfile> SaveProfileAsync(ProxyProfile profile, ProxyCredentials? credentials = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.AuthenticationEnabled && credentials != null)
        {
            if (string.IsNullOrWhiteSpace(profile.CredentialReference))
            {
                profile.CredentialReference = $"ref_{profile.Id}";
            }
            await _credentialStore.SaveCredentialsAsync(profile.CredentialReference, credentials, ct).ConfigureAwait(false);
        }

        await _profileRepo.SaveAsync(profile, ct).ConfigureAwait(false);
        return profile;
    }

    public async Task<bool> DeleteProfileAsync(string id, CancellationToken ct = default)
    {
        var profile = await _profileRepo.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (profile?.CredentialReference != null)
        {
            await _credentialStore.DeleteCredentialsAsync(profile.CredentialReference, ct).ConfigureAwait(false);
        }

        var state = await _stateManager.GetStateAsync(ct).ConfigureAwait(false);
        if (string.Equals(state.ActiveProfileId, id, StringComparison.OrdinalIgnoreCase))
        {
            state.ActiveProfileId = null;
            await _stateManager.SaveStateAsync(state, ct).ConfigureAwait(false);
        }

        return await _profileRepo.DeleteAsync(id, ct).ConfigureAwait(false);
    }

    public async Task SetActiveProfileAsync(string id, CancellationToken ct = default)
    {
        var profile = await _profileRepo.GetByIdAsync(id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Profile with ID '{id}' not found.");

        var state = await _stateManager.GetStateAsync(ct).ConfigureAwait(false);
        state.ActiveProfileId = profile.Id;
        await _stateManager.SaveStateAsync(state, ct).ConfigureAwait(false);
    }

    public async Task<ProxyProfile?> GetActiveProfileAsync(CancellationToken ct = default)
    {
        var state = await _stateManager.GetStateAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(state.ActiveProfileId))
        {
            var all = await _profileRepo.GetAllAsync(ct).ConfigureAwait(false);
            return all.FirstOrDefault();
        }

        return await _profileRepo.GetByIdAsync(state.ActiveProfileId, ct).ConfigureAwait(false);
    }

    public async Task<ProxyTestResult> TestProfileAsync(string? profileId = null, ProxyTestOptions? options = null, CancellationToken ct = default)
    {
        ProxyProfile? profile;
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            profile = await _profileRepo.GetByIdAsync(profileId, ct).ConfigureAwait(false);
        }
        else
        {
            profile = await GetActiveProfileAsync(ct).ConfigureAwait(false);
        }

        if (profile == null)
        {
            return ProxyTestResult.Failure(
                ProxyTestStage.Configuration,
                ProxyTestErrorCode.InvalidConfig,
                "No profile specified or available for testing.");
        }

        ProxyCredentials? creds = null;
        if (profile.AuthenticationEnabled && !string.IsNullOrWhiteSpace(profile.CredentialReference))
        {
            creds = await _credentialStore.GetCredentialsAsync(profile.CredentialReference, ct).ConfigureAwait(false);
        }

        var result = await _proxyTester.TestAsync(profile, creds, options, ct).ConfigureAwait(false);

        // Record in runtime state
        var state = await _stateManager.GetStateAsync(ct).ConfigureAwait(false);
        state.LastTestResult = result;
        state.LastTestedAt = DateTimeOffset.UtcNow;
        await _stateManager.SaveStateAsync(state, ct).ConfigureAwait(false);

        return result;
    }

    public async Task<ProxyApplyResult> ConnectAsync(string? profileId = null, bool force = false, CancellationToken ct = default)
    {
        ProxyProfile? profile;
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            profile = await _profileRepo.GetByIdAsync(profileId, ct).ConfigureAwait(false);
        }
        else
        {
            profile = await GetActiveProfileAsync(ct).ConfigureAwait(false);
        }

        if (profile == null)
        {
            return ProxyApplyResult.Failed("No proxy profile selected for activation.");
        }

        var validation = _validator.Validate(profile);
        if (!validation.IsValid)
        {
            return ProxyApplyResult.Failed($"Cannot activate invalid profile: {string.Join("; ", validation.Errors)}");
        }

        if (!_platformAdapter.IsSupported)
        {
            return ProxyApplyResult.Failed(_platformAdapter.UnsupportedReason ?? "Platform does not support system proxy changes.");
        }

        var state = await _stateManager.GetStateAsync(ct).ConfigureAwait(false);
        var currentOs = await _platformAdapter.GetCurrentSettingsAsync(ct).ConfigureAwait(false);

        // Conflict check (FR-12): if system proxy is already enabled and not managed by us
        if (currentOs.Enabled && !state.IsSystemProxyApplied && !force)
        {
            return ProxyApplyResult.Conflict(
                $"System proxy is already active externally ({currentOs.HttpHost}:{currentOs.HttpPort}). Use --force to overwrite.");
        }

        // Snapshot previous configuration before applying if not already saved
        if (state.RestorableConfiguration == null || !state.IsSystemProxyApplied)
        {
            state.RestorableConfiguration = currentOs;
        }

        var applyResult = await _platformAdapter.ApplyAsync(profile, ct).ConfigureAwait(false);
        if (!applyResult.Success)
        {
            // Do not leave profile marked active when activation failed (FR-11)
            state.HasPendingRecovery = true;
            state.RecoveryMessage = $"Activation failed for profile '{profile.Name}': {applyResult.Message}";
            await _stateManager.SaveStateAsync(state, ct).ConfigureAwait(false);
            return applyResult;
        }

        state.IsSystemProxyApplied = true;
        state.ActiveProfileId = profile.Id;
        state.AppliedEndpoint = $"{profile.Host}:{profile.Port}";
        state.HasPendingRecovery = false;
        state.RecoveryMessage = null;
        await _stateManager.SaveStateAsync(state, ct).ConfigureAwait(false);

        return applyResult;
    }

    public async Task<ProxyRestoreResult> DisconnectAsync(bool force = false, CancellationToken ct = default)
    {
        if (!_platformAdapter.IsSupported)
        {
            return ProxyRestoreResult.Failed(_platformAdapter.UnsupportedReason ?? "Platform unsupported.");
        }

        var state = await _stateManager.GetStateAsync(ct).ConfigureAwait(false);
        var currentOs = await _platformAdapter.GetCurrentSettingsAsync(ct).ConfigureAwait(false);

        // Check if settings were modified externally since applied (FR-12)
        if (state.IsSystemProxyApplied && !string.IsNullOrWhiteSpace(state.AppliedEndpoint))
        {
            var parts = state.AppliedEndpoint.Split(':');
            var appliedHost = parts[0];
            int.TryParse(parts.Length > 1 ? parts[1] : "0", out var appliedPort);

            if (!currentOs.IsEquivalentTo(appliedHost, appliedPort) && !force)
            {
                return ProxyRestoreResult.Failed(
                    "System proxy was modified externally since ErgoProxy applied it. Use --force to restore saved configuration.");
            }
        }

        var restorable = state.RestorableConfiguration ?? new SystemProxyConfiguration { Enabled = false };
        var restoreResult = await _platformAdapter.RestoreAsync(restorable, ct).ConfigureAwait(false);

        if (restoreResult.Success)
        {
            state.IsSystemProxyApplied = false;
            state.AppliedEndpoint = null;
            state.RestorableConfiguration = null;
            state.HasPendingRecovery = false;
            state.RecoveryMessage = null;
            await _stateManager.SaveStateAsync(state, ct).ConfigureAwait(false);
        }
        else
        {
            state.HasPendingRecovery = true;
            state.RecoveryMessage = $"Failed to restore previous proxy settings: {restoreResult.Message}";
            await _stateManager.SaveStateAsync(state, ct).ConfigureAwait(false);
        }

        return restoreResult;
    }

    public async Task<ProxyStatusInfo> GetStatusAsync(CancellationToken ct = default)
    {
        var state = await _stateManager.GetStateAsync(ct).ConfigureAwait(false);
        var activeProfile = await GetActiveProfileAsync(ct).ConfigureAwait(false);
        var currentOs = await _platformAdapter.GetCurrentSettingsAsync(ct).ConfigureAwait(false);

        var configuredEndpoint = activeProfile != null ? $"{activeProfile.Host}:{activeProfile.Port}" : null;
        var isActuallyActive = currentOs.Enabled;

        var hasConflict = false;
        string? conflictMessage = null;

        if (state.IsSystemProxyApplied && state.AppliedEndpoint != null)
        {
            var parts = state.AppliedEndpoint.Split(':');
            var expectedHost = parts[0];
            int.TryParse(parts.Length > 1 ? parts[1] : "0", out var expectedPort);

            if (!currentOs.IsEquivalentTo(expectedHost, expectedPort))
            {
                hasConflict = true;
                conflictMessage = $"System settings mismatch: ErgoProxy applied '{state.AppliedEndpoint}', but OS reports '{currentOs.HttpHost}:{currentOs.HttpPort}'.";
            }
        }

        return new ProxyStatusInfo(
            ActiveProfile: activeProfile,
            ConfiguredEndpoint: configuredEndpoint,
            IsSystemProxyApplied: state.IsSystemProxyApplied,
            IsSystemProxyActuallyActive: isActuallyActive,
            LastTestResult: state.LastTestResult,
            LastTestedAt: state.LastTestedAt,
            PlatformName: _platformAdapter.DesktopEnvironment,
            PlatformSupported: _platformAdapter.IsSupported,
            HasConflict: hasConflict,
            ConflictMessage: conflictMessage,
            HasPendingRecovery: state.HasPendingRecovery,
            RecoveryMessage: state.RecoveryMessage);
    }

    public Task SaveCredentialsAsync(string reference, ProxyCredentials credentials, CancellationToken ct = default)
    {
        return _credentialStore.SaveCredentialsAsync(reference, credentials, ct);
    }

    public Task<ProxyCredentials?> GetCredentialsAsync(string reference, CancellationToken ct = default)
    {
        return _credentialStore.GetCredentialsAsync(reference, ct);
    }
}

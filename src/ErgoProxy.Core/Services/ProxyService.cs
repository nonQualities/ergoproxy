using ErgoProxy.Core.Credentials;
using ErgoProxy.Core.Daemon;
using ErgoProxy.Core.Models;
using ErgoProxy.Core.Network;
using ErgoProxy.Core.Storage;
using ErgoProxy.Core.Validation;

namespace ErgoProxy.Core.Services;

public sealed class ProxyService : IProxyService
{
    private readonly IProfileRepository _profileRepo;
    private readonly IStateManager _stateManager;
    private readonly ICredentialStore _credentialStore;
    private readonly IProxyTester _proxyTester;
    private readonly DaemonClient _daemonClient;
    private readonly IProfileValidator _validator;

    public ProxyService(
        IProfileRepository profileRepo,
        IStateManager stateManager,
        ICredentialStore credentialStore,
        IProxyTester proxyTester,
        DaemonClient? daemonClient = null,
        IProfileValidator? validator = null)
    {
        _profileRepo = profileRepo;
        _stateManager = stateManager;
        _credentialStore = credentialStore;
        _proxyTester = proxyTester;
        _daemonClient = daemonClient ?? new DaemonClient();
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

        if (profile.AuthenticationEnabled && string.IsNullOrWhiteSpace(profile.CredentialReference))
        {
            profile.CredentialReference = $"ref_{profile.Id}";
        }

        var validation = _validator.Validate(profile);
        if (!validation.IsValid)
        {
            throw new ArgumentException($"Cannot save invalid profile: {string.Join("; ", validation.Errors)}");
        }

        if (profile.AuthenticationEnabled && credentials != null)
        {
            await _credentialStore.SaveCredentialsAsync(profile.CredentialReference!, credentials, ct).ConfigureAwait(false);
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

    public async Task<TunnelOperationResult> ConnectAsync(string? profileId = null, CancellationToken ct = default)
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
            return new TunnelOperationResult(false, "No proxy profile selected for activation.");
        }

        var validation = _validator.Validate(profile);
        if (!validation.IsValid)
        {
            return new TunnelOperationResult(false, $"Cannot activate invalid profile: {string.Join("; ", validation.Errors)}");
        }

        ProxyCredentials? creds = null;
        if (profile.AuthenticationEnabled && !string.IsNullOrWhiteSpace(profile.CredentialReference))
        {
            creds = await _credentialStore.GetCredentialsAsync(profile.CredentialReference, ct).ConfigureAwait(false);
        }

        // Ensure background helper daemon is active
        var (daemonReady, daemonMsg) = await DaemonLauncher.EnsureRunningAsync(_daemonClient, ct).ConfigureAwait(false);
        if (!daemonReady)
        {
            return new TunnelOperationResult(false, $"Tunnel service unavailable: {daemonMsg}");
        }

        var startReq = new TunnelStartRequest
        {
            ProfileName = profile.Name,
            ProxyHost = profile.Host,
            ProxyPort = profile.Port,
            Username = creds?.Username,
            Password = creds?.Password,
            BypassRules = profile.BypassRules
        };

        var startRes = await _daemonClient.StartAsync(startReq, ct).ConfigureAwait(false);
        if (startRes.Success)
        {
            var state = await _stateManager.GetStateAsync(ct).ConfigureAwait(false);
            state.ActiveProfileId = profile.Id;
            state.HasPendingRecovery = false;
            state.RecoveryMessage = null;
            await _stateManager.SaveStateAsync(state, ct).ConfigureAwait(false);
        }

        return new TunnelOperationResult(startRes.Success, startRes.Message, startRes.Status);
    }

    public async Task<TunnelOperationResult> DisconnectAsync(CancellationToken ct = default)
    {
        var stopRes = await _daemonClient.StopAsync(ct).ConfigureAwait(false);
        if (stopRes.Success)
        {
            var state = await _stateManager.GetStateAsync(ct).ConfigureAwait(false);
            state.HasPendingRecovery = false;
            state.RecoveryMessage = null;
            await _stateManager.SaveStateAsync(state, ct).ConfigureAwait(false);
        }

        return new TunnelOperationResult(stopRes.Success, stopRes.Message, stopRes.Status);
    }

    public async Task<ProxyStatusInfo> GetStatusAsync(CancellationToken ct = default)
    {
        var state = await _stateManager.GetStateAsync(ct).ConfigureAwait(false);
        var activeProfile = await GetActiveProfileAsync(ct).ConfigureAwait(false);
        var tunnelStatus = await _daemonClient.GetStatusAsync(ct).ConfigureAwait(false) ?? TunnelStatus.NotRunning();

        return new ProxyStatusInfo(
            ActiveProfile: activeProfile,
            TunnelStatus: tunnelStatus,
            LastTestResult: state.LastTestResult,
            LastTestedAt: state.LastTestedAt,
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

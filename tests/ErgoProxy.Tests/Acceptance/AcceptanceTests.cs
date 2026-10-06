using ErgoProxy.Cli.Commands;
using ErgoProxy.Core.Credentials;
using ErgoProxy.Core.Models;
using ErgoProxy.Core.Network;
using ErgoProxy.Core.Security;
using ErgoProxy.Core.Services;
using ErgoProxy.Core.Storage;
using ErgoProxy.Core.Validation;
using ErgoProxy.Tests.Helpers;
using Xunit;

namespace ErgoProxy.Tests.Acceptance;

public class AcceptanceTests : IDisposable
{
    private readonly string _testDir;
    private readonly JsonProfileRepository _profileRepo;
    private readonly JsonStateManager _stateManager;
    private readonly EncryptedFileCredentialStore _credentialStore;
    private readonly ProfileValidator _validator;
    private readonly ProxyTester _proxyTester;
    private readonly MockPlatformAdapter _mockPlatform;
    private readonly ProxyService _proxyService;

    public AcceptanceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ergoproxy_at_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        _profileRepo = new JsonProfileRepository(Path.Combine(_testDir, "profiles.json"));
        _stateManager = new JsonStateManager(Path.Combine(_testDir, "state.json"));
        _credentialStore = new EncryptedFileCredentialStore(Path.Combine(_testDir, "vault.enc"));
        _validator = new ProfileValidator();
        _proxyTester = new ProxyTester(_validator);
        _mockPlatform = new MockPlatformAdapter();

        _proxyService = new ProxyService(
            _profileRepo,
            _stateManager,
            _credentialStore,
            _proxyTester,
            _mockPlatform,
            _validator);
    }

    [Fact]
    public async Task AT_01_CreateValidProfile_ProfileIsValidatedAndPersisted()
    {
        var profile = new ProxyProfile
        {
            Id = "at-01-profile",
            Name = "Campus Main",
            Host = "127.0.0.1",
            Port = 8080,
            BypassRules = new List<string> { "localhost", "127.0.0.1" }
        };

        var saved = await _proxyService.SaveProfileAsync(profile);
        Assert.NotNull(saved);

        var retrieved = await _proxyService.GetProfileAsync("at-01-profile");
        Assert.NotNull(retrieved);
        Assert.Equal("Campus Main", retrieved.Name);
        Assert.Equal("127.0.0.1", retrieved.Host);
        Assert.Equal(8080, retrieved.Port);
    }

    [Fact]
    public async Task AT_02_InvalidPort_RejectedWithExplanatoryError()
    {
        var profile = new ProxyProfile
        {
            Id = "at-02-profile",
            Name = "Invalid Port Profile",
            Host = "127.0.0.1",
            Port = 70000 // Out of TCP range
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _proxyService.SaveProfileAsync(profile));
        Assert.Contains("Port must be between 1 and 65535", ex.Message);
    }

    [Fact]
    public async Task AT_03_MissingHost_ProfileCannotBeActivated()
    {
        var profile = new ProxyProfile
        {
            Id = "at-03-profile",
            Name = "Missing Host Profile",
            Host = "",
            Port = 8080
        };

        // Cannot be saved
        await Assert.ThrowsAsync<ArgumentException>(() => _proxyService.SaveProfileAsync(profile));

        // Direct connect validation rejection
        var connectResult = await _proxyService.ConnectAsync(profile.Id);
        Assert.False(connectResult.Success);
    }

    [Fact]
    public async Task AT_04_ProxyWithoutAuthentication_ConnectivityTestSucceeds()
    {
        await using var testProxy = new TestProxyServer { RequireAuth = false };

        var profile = new ProxyProfile
        {
            Id = "at-04-profile",
            Name = "Open Proxy",
            Host = testProxy.Host,
            Port = testProxy.Port,
            AuthenticationEnabled = false
        };

        await _proxyService.SaveProfileAsync(profile);

        var options = new ProxyTestOptions
        {
            HttpTestUrl = $"http://{testProxy.Host}:{testProxy.Port}/test",
            TestHttpsConnectTunnel = false
        };

        var result = await _proxyService.TestProfileAsync(profile.Id, options);
        Assert.True(result.IsSuccess);
        Assert.Equal(ProxyTestErrorCode.None, result.ErrorCode);
    }

    [Fact]
    public async Task AT_05_AuthenticatedProxy_ValidCredentialsSucceed_InvalidCredentialsFail()
    {
        await using var testProxy = new TestProxyServer
        {
            RequireAuth = true,
            ExpectedUser = "student",
            ExpectedPassword = "mypassword123"
        };

        // 1. Valid credentials
        var validProfile = new ProxyProfile
        {
            Id = "at-05-valid",
            Name = "Auth Proxy",
            Host = testProxy.Host,
            Port = testProxy.Port,
            AuthenticationEnabled = true
        };
        var validCreds = new ProxyCredentials("student", "mypassword123");
        await _proxyService.SaveProfileAsync(validProfile, validCreds);

        var options = new ProxyTestOptions
        {
            HttpTestUrl = $"http://{testProxy.Host}:{testProxy.Port}/test",
            TestHttpsConnectTunnel = false
        };

        var validResult = await _proxyService.TestProfileAsync(validProfile.Id, options);
        Assert.True(validResult.IsSuccess);

        // 2. Invalid credentials
        var invalidProfile = new ProxyProfile
        {
            Id = "at-05-invalid",
            Name = "Bad Auth Proxy",
            Host = testProxy.Host,
            Port = testProxy.Port,
            AuthenticationEnabled = true
        };
        var invalidCreds = new ProxyCredentials("student", "wrongPassword");
        await _proxyService.SaveProfileAsync(invalidProfile, invalidCreds);

        var invalidResult = await _proxyService.TestProfileAsync(invalidProfile.Id, options);
        Assert.False(invalidResult.IsSuccess);
        Assert.Equal(ProxyTestStage.ProxyAuth, invalidResult.Stage);
        Assert.Equal(ProxyTestErrorCode.AuthFailed, invalidResult.ErrorCode);
        Assert.Equal(407, invalidResult.HttpStatusCode);
    }

    [Fact]
    public async Task AT_06_UnreachableEndpoint_TerminatesWithinTimeoutAndReportsFailure()
    {
        // Non-routable address (TEST-NET-1 RFC 5737) to test timeout bounded behavior
        var profile = new ProxyProfile
        {
            Id = "at-06-timeout",
            Name = "Unreachable",
            Host = "192.0.2.1",
            Port = 8080
        };
        await _proxyService.SaveProfileAsync(profile);

        var options = new ProxyTestOptions
        {
            Timeout = TimeSpan.FromMilliseconds(500),
            TestHttpsConnectTunnel = false
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await _proxyService.TestProfileAsync(profile.Id, options);
        sw.Stop();

        Assert.False(result.IsSuccess);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"Test exceeded timeout: took {sw.Elapsed.TotalSeconds}s");
    }

    [Fact]
    public async Task AT_07_HttpsTunnelling_SuccessfulConnectAndTlsReported()
    {
        await using var testProxy = new TestProxyServer
        {
            RequireAuth = false
        };

        var profile = new ProxyProfile
        {
            Id = "at-07-connect",
            Name = "Tunnel Proxy",
            Host = testProxy.Host,
            Port = testProxy.Port,
            AuthenticationEnabled = false
        };
        await _proxyService.SaveProfileAsync(profile);

        var options = new ProxyTestOptions
        {
            HttpTestUrl = $"http://{testProxy.Host}:{testProxy.Port}/test",
            HttpsTestHost = "localhost",
            HttpsTestPort = testProxy.Port,
            TestHttpsConnectTunnel = true,
            ValidateTlsCertificate = false // In-memory self-signed test cert
        };

        var result = await _proxyService.TestProfileAsync(profile.Id, options);
        Assert.True(result.IsSuccess);
        Assert.True(result.ConnectTunnelSuccess);
    }

    [Fact]
    public async Task AT_08_EnableSystemProxy_IntendedSettingsAppliedAndVerified()
    {
        var profile = new ProxyProfile
        {
            Id = "at-08-profile",
            Name = "System Proxy Profile",
            Host = "10.0.0.50",
            Port = 8080,
            BypassRules = new List<string> { "localhost", "10.0.0.0/8" }
        };
        await _proxyService.SaveProfileAsync(profile);

        var result = await _proxyService.ConnectAsync(profile.Id);
        Assert.True(result.Success);

        // Verify with mock platform
        var currentOs = await _mockPlatform.GetCurrentSettingsAsync();
        Assert.True(currentOs.Enabled);
        Assert.Equal("10.0.0.50", currentOs.HttpHost);
        Assert.Equal(8080, currentOs.HttpPort);

        // Verify state
        var state = await _stateManager.GetStateAsync();
        Assert.True(state.IsSystemProxyApplied);
        Assert.Equal("at-08-profile", state.ActiveProfileId);
    }

    [Fact]
    public async Task AT_09_DisableSystemProxy_PreviousSettingsRestoredSafely()
    {
        // Initial OS state: Proxy was previously pointed to old.corp.proxy:3128
        _mockPlatform.CurrentSettings = new SystemProxyConfiguration
        {
            Enabled = true,
            HttpHost = "old.corp.proxy",
            HttpPort = 3128,
            BypassRules = new List<string> { "corp.local" }
        };

        var profile = new ProxyProfile
        {
            Id = "at-09-profile",
            Name = "New Proxy",
            Host = "new.proxy",
            Port = 8080
        };
        await _proxyService.SaveProfileAsync(profile);

        // Enable new proxy (with force since existing was enabled)
        var connectResult = await _proxyService.ConnectAsync(profile.Id, force: true);
        Assert.True(connectResult.Success);
        Assert.Equal("new.proxy", _mockPlatform.CurrentSettings.HttpHost);

        // Disable proxy
        var disconnectResult = await _proxyService.DisconnectAsync();
        Assert.True(disconnectResult.Success);

        // Assert previous settings were faithfully restored!
        Assert.True(_mockPlatform.CurrentSettings.Enabled);
        Assert.Equal("old.corp.proxy", _mockPlatform.CurrentSettings.HttpHost);
        Assert.Equal(3128, _mockPlatform.CurrentSettings.HttpPort);
        Assert.Contains("corp.local", _mockPlatform.CurrentSettings.BypassRules);

        var state = await _stateManager.GetStateAsync();
        Assert.False(state.IsSystemProxyApplied);
    }

    [Fact]
    public async Task AT_10_ExternalSettingsChanged_DetectsConflictAvoidsOverwriting()
    {
        var profile = new ProxyProfile
        {
            Id = "at-10-profile",
            Name = "Test Conflict",
            Host = "managed.proxy",
            Port = 8080
        };
        await _proxyService.SaveProfileAsync(profile);

        // 1. External settings already active before apply
        _mockPlatform.CurrentSettings.Enabled = true;
        _mockPlatform.CurrentSettings.HttpHost = "external.configured.proxy";
        _mockPlatform.CurrentSettings.HttpPort = 9000;

        // Apply without force should detect conflict
        var result = await _proxyService.ConnectAsync(profile.Id, force: false);
        Assert.False(result.Success);
        Assert.True(result.HasConflict);

        // Apply with force should proceed
        var forceResult = await _proxyService.ConnectAsync(profile.Id, force: true);
        Assert.True(forceResult.Success);

        // 2. External tampering while active
        _mockPlatform.CurrentSettings.HttpHost = "tampered.proxy";
        _mockPlatform.CurrentSettings.HttpPort = 4444;

        // Disconnect without force should detect external tampering
        var disconnectResult = await _proxyService.DisconnectAsync(force: false);
        Assert.False(disconnectResult.Success);
        Assert.Contains("modified externally", disconnectResult.Message);

        // Force disconnect succeeds
        var forceDisconnect = await _proxyService.DisconnectAsync(force: true);
        Assert.True(forceDisconnect.Success);
    }

    [Fact]
    public async Task AT_11_SecretRedaction_PasswordsDoNotAppearInLogsOrPlaintextFiles()
    {
        var profile = new ProxyProfile
        {
            Id = "at-11-profile",
            Name = "Secure Profile",
            Host = "proxy.secure.net",
            Port = 8080,
            AuthenticationEnabled = true
        };
        var creds = new ProxyCredentials("admin", "TopSecretSuperPassword999!");
        await _proxyService.SaveProfileAsync(profile, creds);

        // 1. Check profile.json on disk does NOT contain the password
        var profileJson = await File.ReadAllTextAsync(Path.Combine(_testDir, "profiles.json"));
        Assert.DoesNotContain("TopSecretSuperPassword999!", profileJson);

        // 2. Check ProxyCredentials.ToString() redacts the password
        Assert.DoesNotContain("TopSecretSuperPassword999!", creds.ToString());
        Assert.Contains("***", creds.ToString());

        // 3. Check SecretRedactor utility
        var rawLog = $"Failed to authenticate with pass: TopSecretSuperPassword999! on http://admin:TopSecretSuperPassword999!@proxy.secure.net";
        var redactedLog = SecretRedactor.Redact(rawLog);
        Assert.DoesNotContain("TopSecretSuperPassword999!", redactedLog);
    }

    [Fact]
    public async Task AT_12_UnsupportedEnvironment_DeclinesUnsupportedChangesAndExplains()
    {
        _mockPlatform.IsSupported = false;
        _mockPlatform.UnsupportedReason = "Headless environment without desktop proxy daemon.";

        var profile = new ProxyProfile
        {
            Id = "at-12-profile",
            Name = "Headless Profile",
            Host = "10.0.0.1",
            Port = 8080
        };
        await _proxyService.SaveProfileAsync(profile);

        var result = await _proxyService.ConnectAsync(profile.Id);
        Assert.False(result.Success);
        Assert.Contains("Headless environment", result.Message);
    }

    [Fact]
    public async Task AT_13_RestartApplication_ProfilesRemainAvailableAndStateAccurate()
    {
        var profile = new ProxyProfile
        {
            Id = "at-13-persisted",
            Name = "Persistent Profile",
            Host = "proxy.persist.org",
            Port = 8888
        };
        await _proxyService.SaveProfileAsync(profile);
        await _proxyService.ConnectAsync(profile.Id);

        // Simulate app restart by constructing new instances pointing to same files
        var newProfileRepo = new JsonProfileRepository(Path.Combine(_testDir, "profiles.json"));
        var newStateManager = new JsonStateManager(Path.Combine(_testDir, "state.json"));
        var newService = new ProxyService(
            newProfileRepo,
            newStateManager,
            _credentialStore,
            _proxyTester,
            _mockPlatform,
            _validator);

        var loadedProfile = await newService.GetProfileAsync("at-13-persisted");
        Assert.NotNull(loadedProfile);
        Assert.Equal("Persistent Profile", loadedProfile.Name);

        var status = await newService.GetStatusAsync();
        Assert.True(status.IsSystemProxyApplied);
        Assert.Equal("proxy.persist.org:8888", status.ConfiguredEndpoint);
    }

    [Fact]
    public async Task AT_14_InterruptedActivation_RollsBackAndReportsPendingRecovery()
    {
        _mockPlatform.ShouldFailVerification = true;

        var profile = new ProxyProfile
        {
            Id = "at-14-fail",
            Name = "Failing Profile",
            Host = "fail.proxy",
            Port = 8080
        };
        await _proxyService.SaveProfileAsync(profile);

        var result = await _proxyService.ConnectAsync(profile.Id);
        Assert.False(result.Success);

        // Verification failure must NOT leave profile marked active (FR-11)
        var state = await _stateManager.GetStateAsync();
        Assert.False(state.IsSystemProxyApplied);
        Assert.True(state.HasPendingRecovery);
        Assert.NotNull(state.RecoveryMessage);
    }

    [Fact]
    public async Task AT_15_CliOperation_NonInteractiveCommandsReturnExitCodes()
    {
        // 1. configure command succeeds with exit code 0
        var cfgArgs = new[]
        {
            "--name", "CLI-Profile",
            "--host", "127.0.0.1",
            "--port", "8080"
        };
        var cfgCode = await ConfigureCommand.ExecuteAsync(_proxyService, cfgArgs);
        Assert.Equal(0, cfgCode);

        // 2. configure command with invalid port returns exit code 1
        var badCfgArgs = new[]
        {
            "--name", "Bad-Port",
            "--host", "127.0.0.1",
            "--port", "-5"
        };
        var badCfgCode = await ConfigureCommand.ExecuteAsync(_proxyService, badCfgArgs);
        Assert.Equal(1, badCfgCode);

        // 3. status command succeeds with exit code 0
        var statusCode = await StatusCommand.ExecuteAsync(_proxyService, new[] { "--json" });
        Assert.Equal(0, statusCode);

        // 4. profiles command succeeds with exit code 0
        var profilesCode = await ProfilesCommand.ExecuteAsync(_proxyService, new[] { "--json" });
        Assert.Equal(0, profilesCode);

        // 5. connect command succeeds with exit code 0
        var connectCode = await ConnectCommand.ExecuteAsync(_proxyService, Array.Empty<string>());
        Assert.Equal(0, connectCode);

        // 6. disconnect command succeeds with exit code 0
        var disconnectCode = await DisconnectCommand.ExecuteAsync(_proxyService, Array.Empty<string>());
        Assert.Equal(0, disconnectCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }
    }
}

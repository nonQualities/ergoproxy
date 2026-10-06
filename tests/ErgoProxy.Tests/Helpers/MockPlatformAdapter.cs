using ErgoProxy.Core.Models;
using ErgoProxy.Core.Platform;

namespace ErgoProxy.Tests.Helpers;

public sealed class MockPlatformAdapter : IPlatformAdapter
{
    public PlatformType CurrentPlatform { get; set; } = PlatformType.Linux;
    public string DesktopEnvironment { get; set; } = "MockDesktop";
    public bool IsSupported { get; set; } = true;
    public string? UnsupportedReason { get; set; }

    public SystemProxyConfiguration CurrentSettings { get; set; } = new()
    {
        Enabled = false,
        SourcePlatform = "Mock",
        CapturedAt = DateTimeOffset.UtcNow
    };

    public bool ShouldFailVerification { get; set; }
    public bool ShouldFailApply { get; set; }
    public string? ApplyFailureMessage { get; set; }

    public List<ProxyProfile> AppliedProfiles { get; } = new();
    public List<SystemProxyConfiguration> RestoredSettings { get; } = new();

    public Task<SystemProxyConfiguration> GetCurrentSettingsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new SystemProxyConfiguration
        {
            Enabled = CurrentSettings.Enabled,
            HttpHost = CurrentSettings.HttpHost,
            HttpPort = CurrentSettings.HttpPort,
            HttpsHost = CurrentSettings.HttpsHost,
            HttpsPort = CurrentSettings.HttpsPort,
            BypassRules = new List<string>(CurrentSettings.BypassRules),
            SourcePlatform = CurrentSettings.SourcePlatform,
            RawSettings = new Dictionary<string, string>(CurrentSettings.RawSettings),
            CapturedAt = CurrentSettings.CapturedAt
        });
    }

    public Task<ProxyApplyResult> ApplyAsync(ProxyProfile profile, CancellationToken ct = default)
    {
        if (!IsSupported)
        {
            return Task.FromResult(ProxyApplyResult.Failed(UnsupportedReason ?? "Unsupported platform."));
        }

        if (ShouldFailApply)
        {
            return Task.FromResult(ProxyApplyResult.Failed(ApplyFailureMessage ?? "Simulated apply failure."));
        }

        var previous = new SystemProxyConfiguration
        {
            Enabled = CurrentSettings.Enabled,
            HttpHost = CurrentSettings.HttpHost,
            HttpPort = CurrentSettings.HttpPort,
            HttpsHost = CurrentSettings.HttpsHost,
            HttpsPort = CurrentSettings.HttpsPort,
            BypassRules = new List<string>(CurrentSettings.BypassRules),
            SourcePlatform = CurrentSettings.SourcePlatform,
            RawSettings = new Dictionary<string, string>(CurrentSettings.RawSettings),
            CapturedAt = CurrentSettings.CapturedAt
        };

        CurrentSettings.Enabled = true;
        CurrentSettings.HttpHost = profile.Host;
        CurrentSettings.HttpPort = profile.Port;
        CurrentSettings.HttpsHost = profile.Host;
        CurrentSettings.HttpsPort = profile.Port;
        CurrentSettings.BypassRules = new List<string>(profile.BypassRules);

        AppliedProfiles.Add(profile.Clone());

        if (ShouldFailVerification)
        {
            // rollback
            CurrentSettings.Enabled = previous.Enabled;
            CurrentSettings.HttpHost = previous.HttpHost;
            CurrentSettings.HttpPort = previous.HttpPort;
            return Task.FromResult(ProxyApplyResult.Failed("Verification failed after applying proxy settings; previous settings were restored."));
        }

        return Task.FromResult(ProxyApplyResult.Succeeded($"Mock applied proxy {profile.Host}:{profile.Port}", previous));
    }

    public Task<ProxyRestoreResult> RestoreAsync(SystemProxyConfiguration previousSettings, CancellationToken ct = default)
    {
        if (!IsSupported)
        {
            return Task.FromResult(ProxyRestoreResult.Failed(UnsupportedReason ?? "Unsupported platform."));
        }

        RestoredSettings.Add(previousSettings);

        CurrentSettings.Enabled = previousSettings.Enabled;
        CurrentSettings.HttpHost = previousSettings.HttpHost;
        CurrentSettings.HttpPort = previousSettings.HttpPort;
        CurrentSettings.HttpsHost = previousSettings.HttpsHost;
        CurrentSettings.HttpsPort = previousSettings.HttpsPort;
        CurrentSettings.BypassRules = new List<string>(previousSettings.BypassRules);

        return Task.FromResult(ProxyRestoreResult.Succeeded("Mock settings restored."));
    }

    public Task<bool> VerifyAsync(ProxyProfile profile, CancellationToken ct = default)
    {
        if (ShouldFailVerification) return Task.FromResult(false);

        return Task.FromResult(
            CurrentSettings.Enabled &&
            string.Equals(CurrentSettings.HttpHost, profile.Host, StringComparison.OrdinalIgnoreCase) &&
            CurrentSettings.HttpPort == profile.Port);
    }
}

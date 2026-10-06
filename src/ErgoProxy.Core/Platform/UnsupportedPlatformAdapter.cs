using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Platform;

public sealed class UnsupportedPlatformAdapter : IPlatformAdapter
{
    public PlatformType CurrentPlatform => PlatformType.Unsupported;
    public string DesktopEnvironment => "Unsupported Environment";
    public bool IsSupported => false;
    public string? UnsupportedReason => "This operating system or desktop environment is not supported for automatic system proxy configuration. " +
                                        "Please configure application proxy settings manually or export environment variables (http_proxy, https_proxy, all_proxy).";

    public Task<SystemProxyConfiguration> GetCurrentSettingsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new SystemProxyConfiguration
        {
            Enabled = false,
            SourcePlatform = "Unsupported",
            CapturedAt = DateTimeOffset.UtcNow
        });
    }

    public Task<ProxyApplyResult> ApplyAsync(ProxyProfile profile, CancellationToken ct = default)
    {
        return Task.FromResult(ProxyApplyResult.Failed(UnsupportedReason!));
    }

    public Task<ProxyRestoreResult> RestoreAsync(SystemProxyConfiguration previousSettings, CancellationToken ct = default)
    {
        return Task.FromResult(ProxyRestoreResult.Failed(UnsupportedReason!));
    }

    public Task<bool> VerifyAsync(ProxyProfile profile, CancellationToken ct = default)
    {
        return Task.FromResult(false);
    }
}

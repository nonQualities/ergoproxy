using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Platform;

public interface IPlatformAdapter
{
    PlatformType CurrentPlatform { get; }
    string DesktopEnvironment { get; }
    bool IsSupported { get; }
    string? UnsupportedReason { get; }

    Task<SystemProxyConfiguration> GetCurrentSettingsAsync(CancellationToken ct = default);
    Task<ProxyApplyResult> ApplyAsync(ProxyProfile profile, CancellationToken ct = default);
    Task<ProxyRestoreResult> RestoreAsync(SystemProxyConfiguration previousSettings, CancellationToken ct = default);
    Task<bool> VerifyAsync(ProxyProfile profile, CancellationToken ct = default);
}

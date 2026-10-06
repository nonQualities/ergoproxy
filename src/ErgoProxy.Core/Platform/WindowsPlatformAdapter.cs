using System.Runtime.InteropServices;
using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Platform;

public sealed class WindowsPlatformAdapter : IPlatformAdapter
{
    private const string RegistryKeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    public PlatformType CurrentPlatform => PlatformType.Windows;
    public string DesktopEnvironment => "Windows Desktop";
    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public string? UnsupportedReason => IsSupported ? null : "Not running on Microsoft Windows.";

    public async Task<SystemProxyConfiguration> GetCurrentSettingsAsync(CancellationToken ct = default)
    {
        var config = new SystemProxyConfiguration
        {
            SourcePlatform = "Windows-WinINet",
            CapturedAt = DateTimeOffset.UtcNow
        };

        if (!IsSupported) return config;

        var enableRes = await ProcessRunner.RunAsync("reg", new[] { "query", RegistryKeyPath, "/v", "ProxyEnable" }, ct: ct).ConfigureAwait(false);
        config.RawSettings["windows:ProxyEnable"] = enableRes.StandardOutput;
        config.Enabled = enableRes.StandardOutput.Contains("0x1", StringComparison.OrdinalIgnoreCase);

        var serverRes = await ProcessRunner.RunAsync("reg", new[] { "query", RegistryKeyPath, "/v", "ProxyServer" }, ct: ct).ConfigureAwait(false);
        config.RawSettings["windows:ProxyServer"] = serverRes.StandardOutput;

        var overrideRes = await ProcessRunner.RunAsync("reg", new[] { "query", RegistryKeyPath, "/v", "ProxyOverride" }, ct: ct).ConfigureAwait(false);
        config.RawSettings["windows:ProxyOverride"] = overrideRes.StandardOutput;

        // Parse ProxyServer: host:port or http=host:port;https=host:port
        var serverMatch = serverRes.StandardOutput.Split(new[] { "REG_SZ" }, StringSplitOptions.None);
        if (serverMatch.Length > 1)
        {
            var serverVal = serverMatch[1].Trim();
            if (serverVal.Contains(':') && !serverVal.Contains(';'))
            {
                var parts = serverVal.Split(':');
                config.HttpHost = parts[0];
                if (int.TryParse(parts[1], out var p)) config.HttpPort = p;
            }
        }

        return config;
    }

    public async Task<ProxyApplyResult> ApplyAsync(ProxyProfile profile, CancellationToken ct = default)
    {
        if (!IsSupported)
        {
            return ProxyApplyResult.Failed(UnsupportedReason ?? "Windows adapter is only available on Microsoft Windows.");
        }

        var previousSettings = await GetCurrentSettingsAsync(ct).ConfigureAwait(false);
        var proxyEndpoint = $"{profile.Host}:{profile.Port}";
        var bypassOverride = profile.BypassRules.Count > 0
            ? string.Join(";", profile.BypassRules) + ";<local>"
            : "<local>";

        // 1. Set ProxyServer
        await ProcessRunner.RunAsync("reg", new[] { "add", RegistryKeyPath, "/v", "ProxyServer", "/t", "REG_SZ", "/d", proxyEndpoint, "/f" }, ct: ct).ConfigureAwait(false);

        // 2. Set ProxyOverride
        await ProcessRunner.RunAsync("reg", new[] { "add", RegistryKeyPath, "/v", "ProxyOverride", "/t", "REG_SZ", "/d", bypassOverride, "/f" }, ct: ct).ConfigureAwait(false);

        // 3. Set ProxyEnable = 1
        var enableRes = await ProcessRunner.RunAsync("reg", new[] { "add", RegistryKeyPath, "/v", "ProxyEnable", "/t", "REG_DWORD", "/d", "1", "/f" }, ct: ct).ConfigureAwait(false);

        if (enableRes.ExitCode != 0)
        {
            return ProxyApplyResult.Failed($"Failed to set Windows proxy registry values: {enableRes.StandardError}");
        }

        var verified = await VerifyAsync(profile, ct).ConfigureAwait(false);
        if (!verified)
        {
            await RestoreAsync(previousSettings, ct).ConfigureAwait(false);
            return ProxyApplyResult.Failed("Verification of Windows proxy failed; rolled back to previous configuration.");
        }

        return ProxyApplyResult.Succeeded($"Windows WinINet system proxy applied to {proxyEndpoint}.", previousSettings);
    }

    public async Task<ProxyRestoreResult> RestoreAsync(SystemProxyConfiguration previousSettings, CancellationToken ct = default)
    {
        if (!IsSupported)
        {
            return ProxyRestoreResult.Failed(UnsupportedReason ?? "Not on Windows.");
        }

        var wasEnabled = previousSettings.Enabled;
        var enableVal = wasEnabled ? "1" : "0";

        await ProcessRunner.RunAsync("reg", new[] { "add", RegistryKeyPath, "/v", "ProxyEnable", "/t", "REG_DWORD", "/d", enableVal, "/f" }, ct: ct).ConfigureAwait(false);

        if (previousSettings.HttpHost != null && previousSettings.HttpPort != null)
        {
            var serverVal = $"{previousSettings.HttpHost}:{previousSettings.HttpPort}";
            await ProcessRunner.RunAsync("reg", new[] { "add", RegistryKeyPath, "/v", "ProxyServer", "/t", "REG_SZ", "/d", serverVal, "/f" }, ct: ct).ConfigureAwait(false);
        }

        return ProxyRestoreResult.Succeeded("Windows system proxy restored to previous configuration.");
    }

    public async Task<bool> VerifyAsync(ProxyProfile profile, CancellationToken ct = default)
    {
        var current = await GetCurrentSettingsAsync(ct).ConfigureAwait(false);
        return current.Enabled &&
               string.Equals(current.HttpHost, profile.Host, StringComparison.OrdinalIgnoreCase) &&
               current.HttpPort == profile.Port;
    }
}

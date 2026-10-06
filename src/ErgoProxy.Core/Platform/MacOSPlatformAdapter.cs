using System.Runtime.InteropServices;
using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Platform;

public sealed class MacOSPlatformAdapter : IPlatformAdapter
{
    private const string NetworkSetup = "/usr/sbin/networksetup";

    public PlatformType CurrentPlatform => PlatformType.MacOS;
    public string DesktopEnvironment => "macOS Aqua";
    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && File.Exists(NetworkSetup);
    public string? UnsupportedReason => IsSupported ? null : "Not running on macOS or networksetup utility unavailable.";

    private async Task<string> GetPrimaryNetworkServiceAsync(CancellationToken ct)
    {
        try
        {
            var res = await ProcessRunner.RunAsync(NetworkSetup, new[] { "-listnetworkserviceorder" }, ct: ct).ConfigureAwait(false);
            var lines = res.StandardOutput.Split('\n');
            foreach (var line in lines)
            {
                // Format: (Hardware Port: Wi-Fi, Device: en0)
                if (line.Contains("(Hardware Port:") && line.Contains("Device:"))
                {
                    // Preceding line usually has "(1) Wi-Fi"
                }
                if (line.StartsWith("(") && line.Contains(")"))
                {
                    var parts = line.Split(')', 2);
                    if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        var candidate = parts[1].Trim();
                        if (candidate.Equals("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
                            candidate.Equals("Ethernet", StringComparison.OrdinalIgnoreCase))
                        {
                            return candidate;
                        }
                    }
                }
            }
        }
        catch { }

        return "Wi-Fi";
    }

    public async Task<SystemProxyConfiguration> GetCurrentSettingsAsync(CancellationToken ct = default)
    {
        var config = new SystemProxyConfiguration
        {
            SourcePlatform = "macOS-NetworkSetup",
            CapturedAt = DateTimeOffset.UtcNow
        };

        if (!IsSupported) return config;

        var service = await GetPrimaryNetworkServiceAsync(ct).ConfigureAwait(false);
        config.RawSettings["macos:service"] = service;

        var webRes = await ProcessRunner.RunAsync(NetworkSetup, new[] { "-getwebproxy", service }, ct: ct).ConfigureAwait(false);
        config.RawSettings["macos:getwebproxy"] = webRes.StandardOutput;

        var secureRes = await ProcessRunner.RunAsync(NetworkSetup, new[] { "-getsecurewebproxy", service }, ct: ct).ConfigureAwait(false);
        config.RawSettings["macos:getsecurewebproxy"] = secureRes.StandardOutput;

        var bypassRes = await ProcessRunner.RunAsync(NetworkSetup, new[] { "-getproxybypassdomains", service }, ct: ct).ConfigureAwait(false);
        config.RawSettings["macos:getproxybypassdomains"] = bypassRes.StandardOutput;

        config.Enabled = webRes.StandardOutput.Contains("Enabled: Yes", StringComparison.OrdinalIgnoreCase);

        // Parse "Server: <host>" and "Port: <port>"
        foreach (var line in webRes.StandardOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Server:", StringComparison.OrdinalIgnoreCase))
            {
                config.HttpHost = trimmed.Substring(7).Trim();
            }
            else if (trimmed.StartsWith("Port:", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(trimmed.Substring(5).Trim(), out var p)) config.HttpPort = p;
            }
        }

        return config;
    }

    public async Task<ProxyApplyResult> ApplyAsync(ProxyProfile profile, CancellationToken ct = default)
    {
        if (!IsSupported)
        {
            return ProxyApplyResult.Failed(UnsupportedReason ?? "macOS adapter is only available on macOS.");
        }

        var previousSettings = await GetCurrentSettingsAsync(ct).ConfigureAwait(false);
        var service = await GetPrimaryNetworkServiceAsync(ct).ConfigureAwait(false);

        // 1. Set HTTP web proxy
        await ProcessRunner.RunAsync(NetworkSetup, new[] { "-setwebproxy", service, profile.Host, profile.Port.ToString() }, ct: ct).ConfigureAwait(false);

        // 2. Set HTTPS secure web proxy
        await ProcessRunner.RunAsync(NetworkSetup, new[] { "-setsecurewebproxy", service, profile.Host, profile.Port.ToString() }, ct: ct).ConfigureAwait(false);

        // 3. Set bypass domains
        if (profile.BypassRules.Count > 0)
        {
            var bypassArgs = new List<string> { "-setproxybypassdomains", service };
            bypassArgs.AddRange(profile.BypassRules);
            await ProcessRunner.RunAsync(NetworkSetup, bypassArgs, ct: ct).ConfigureAwait(false);
        }

        // 4. Turn proxies ON
        await ProcessRunner.RunAsync(NetworkSetup, new[] { "-setwebproxystate", service, "on" }, ct: ct).ConfigureAwait(false);
        await ProcessRunner.RunAsync(NetworkSetup, new[] { "-setsecurewebproxystate", service, "on" }, ct: ct).ConfigureAwait(false);

        var verified = await VerifyAsync(profile, ct).ConfigureAwait(false);
        if (!verified)
        {
            await RestoreAsync(previousSettings, ct).ConfigureAwait(false);
            return ProxyApplyResult.Failed("Verification of macOS proxy failed; restored previous configuration.");
        }

        return ProxyApplyResult.Succeeded($"macOS system proxy on '{service}' applied to {profile.Host}:{profile.Port}.", previousSettings);
    }

    public async Task<ProxyRestoreResult> RestoreAsync(SystemProxyConfiguration previousSettings, CancellationToken ct = default)
    {
        if (!IsSupported)
        {
            return ProxyRestoreResult.Failed(UnsupportedReason ?? "Not on macOS.");
        }

        var service = previousSettings.RawSettings.GetValueOrDefault("macos:service") ?? await GetPrimaryNetworkServiceAsync(ct).ConfigureAwait(false);

        if (!previousSettings.Enabled)
        {
            await ProcessRunner.RunAsync(NetworkSetup, new[] { "-setwebproxystate", service, "off" }, ct: ct).ConfigureAwait(false);
            await ProcessRunner.RunAsync(NetworkSetup, new[] { "-setsecurewebproxystate", service, "off" }, ct: ct).ConfigureAwait(false);
        }
        else if (previousSettings.HttpHost != null && previousSettings.HttpPort != null)
        {
            await ProcessRunner.RunAsync(NetworkSetup, new[] { "-setwebproxy", service, previousSettings.HttpHost, previousSettings.HttpPort.Value.ToString() }, ct: ct).ConfigureAwait(false);
            await ProcessRunner.RunAsync(NetworkSetup, new[] { "-setsecurewebproxy", service, previousSettings.HttpHost, previousSettings.HttpPort.Value.ToString() }, ct: ct).ConfigureAwait(false);
            await ProcessRunner.RunAsync(NetworkSetup, new[] { "-setwebproxystate", service, "on" }, ct: ct).ConfigureAwait(false);
            await ProcessRunner.RunAsync(NetworkSetup, new[] { "-setsecurewebproxystate", service, "on" }, ct: ct).ConfigureAwait(false);
        }

        return ProxyRestoreResult.Succeeded($"macOS system proxy settings on '{service}' restored.");
    }

    public async Task<bool> VerifyAsync(ProxyProfile profile, CancellationToken ct = default)
    {
        var current = await GetCurrentSettingsAsync(ct).ConfigureAwait(false);
        return current.Enabled &&
               string.Equals(current.HttpHost, profile.Host, StringComparison.OrdinalIgnoreCase) &&
               current.HttpPort == profile.Port;
    }
}

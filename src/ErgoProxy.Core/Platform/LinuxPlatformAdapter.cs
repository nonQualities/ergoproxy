using System.Text.RegularExpressions;
using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Platform;

public sealed class LinuxPlatformAdapter : IPlatformAdapter
{
    public PlatformType CurrentPlatform => PlatformType.Linux;
    public string DesktopEnvironment { get; }
    public bool IsSupported { get; }
    public string? UnsupportedReason { get; }

    private readonly bool _isGnome;
    private readonly bool _isKde;
    private readonly string? _kwriteConfigBin;

    public LinuxPlatformAdapter()
    {
        var desktop = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? 
                       Environment.GetEnvironmentVariable("DESKTOP_SESSION") ?? string.Empty).ToLowerInvariant();

        DesktopEnvironment = string.IsNullOrWhiteSpace(desktop) ? "Headless / Non-desktop" : desktop;

        var hasGsettings = CheckBinaryExists("gsettings");
        var hasKwrite6 = CheckBinaryExists("kwriteconfig6");
        var hasKwrite5 = CheckBinaryExists("kwriteconfig5");

        if (desktop.Contains("gnome") || desktop.Contains("ubuntu") || desktop.Contains("cinnamon") || desktop.Contains("mate") || hasGsettings)
        {
            _isGnome = true;
            IsSupported = true;
            UnsupportedReason = null;
        }
        else if (desktop.Contains("kde") || desktop.Contains("plasma") || hasKwrite6 || hasKwrite5)
        {
            _isKde = true;
            _kwriteConfigBin = hasKwrite6 ? "kwriteconfig6" : "kwriteconfig5";
            IsSupported = true;
            UnsupportedReason = null;
        }
        else
        {
            IsSupported = false;
            UnsupportedReason = "No supported Linux desktop proxy manager (GNOME gsettings or KDE kwriteconfig) detected. " +
                                "For CLI or headless sessions, set proxy environment variables: export http_proxy=... https_proxy=...";
        }
    }

    private static bool CheckBinaryExists(string name)
    {
        try
        {
            var res = ProcessRunner.RunAsync("which", new[] { name }).GetAwaiter().GetResult();
            return res.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<SystemProxyConfiguration> GetCurrentSettingsAsync(CancellationToken ct = default)
    {
        var config = new SystemProxyConfiguration
        {
            SourcePlatform = $"Linux-{DesktopEnvironment}",
            CapturedAt = DateTimeOffset.UtcNow
        };

        if (_isGnome)
        {
            var modeRes = await ProcessRunner.RunAsync("gsettings", new[] { "get", "org.gnome.system.proxy", "mode" }, ct: ct).ConfigureAwait(false);
            var mode = modeRes.StandardOutput.Trim('\'', ' ', '"');
            config.RawSettings["gnome:mode"] = mode;
            config.Enabled = string.Equals(mode, "manual", StringComparison.OrdinalIgnoreCase);

            var httpHostRes = await ProcessRunner.RunAsync("gsettings", new[] { "get", "org.gnome.system.proxy.http", "host" }, ct: ct).ConfigureAwait(false);
            var httpPortRes = await ProcessRunner.RunAsync("gsettings", new[] { "get", "org.gnome.system.proxy.http", "port" }, ct: ct).ConfigureAwait(false);
            config.HttpHost = httpHostRes.StandardOutput.Trim('\'', ' ', '"');
            if (int.TryParse(httpPortRes.StandardOutput.Trim(), out var p)) config.HttpPort = p;

            config.RawSettings["gnome:http:host"] = config.HttpHost ?? string.Empty;
            config.RawSettings["gnome:http:port"] = httpPortRes.StandardOutput.Trim();

            var httpsHostRes = await ProcessRunner.RunAsync("gsettings", new[] { "get", "org.gnome.system.proxy.https", "host" }, ct: ct).ConfigureAwait(false);
            var httpsPortRes = await ProcessRunner.RunAsync("gsettings", new[] { "get", "org.gnome.system.proxy.https", "port" }, ct: ct).ConfigureAwait(false);
            config.HttpsHost = httpsHostRes.StandardOutput.Trim('\'', ' ', '"');
            if (int.TryParse(httpsPortRes.StandardOutput.Trim(), out var ps)) config.HttpsPort = ps;

            config.RawSettings["gnome:https:host"] = config.HttpsHost ?? string.Empty;
            config.RawSettings["gnome:https:port"] = httpsPortRes.StandardOutput.Trim();

            var ignoreRes = await ProcessRunner.RunAsync("gsettings", new[] { "get", "org.gnome.system.proxy", "ignore-hosts" }, ct: ct).ConfigureAwait(false);
            config.RawSettings["gnome:ignore-hosts"] = ignoreRes.StandardOutput.Trim();

            // Parse list: ['localhost', '127.0.0.1']
            var matches = Regex.Matches(ignoreRes.StandardOutput, @"'([^']+)'");
            foreach (Match m in matches)
            {
                config.BypassRules.Add(m.Groups[1].Value);
            }
        }
        else if (_isKde)
        {
            var kwrite = _kwriteConfigBin ?? "kwriteconfig5";
            var kread = kwrite.Replace("write", "read");
            var typeRes = await ProcessRunner.RunAsync(kread, new[] { "--file", "kioslaverc", "--group", "Proxy Settings", "--key", "ProxyType" }, ct: ct).ConfigureAwait(false);
            var httpRes = await ProcessRunner.RunAsync(kread, new[] { "--file", "kioslaverc", "--group", "Proxy Settings", "--key", "httpProxy" }, ct: ct).ConfigureAwait(false);
            var noProxyRes = await ProcessRunner.RunAsync(kread, new[] { "--file", "kioslaverc", "--group", "Proxy Settings", "--key", "NoProxyFor" }, ct: ct).ConfigureAwait(false);

            config.RawSettings["kde:ProxyType"] = typeRes.StandardOutput;
            config.RawSettings["kde:httpProxy"] = httpRes.StandardOutput;
            config.RawSettings["kde:NoProxyFor"] = noProxyRes.StandardOutput;

            config.Enabled = typeRes.StandardOutput.Trim() == "1";
            if (Uri.TryCreate(httpRes.StandardOutput.Trim(), UriKind.Absolute, out var uri))
            {
                config.HttpHost = uri.Host;
                config.HttpPort = uri.Port;
            }
        }

        return config;
    }

    public async Task<ProxyApplyResult> ApplyAsync(ProxyProfile profile, CancellationToken ct = default)
    {
        if (!IsSupported)
        {
            return ProxyApplyResult.Failed(UnsupportedReason ?? "Current Linux desktop environment is not supported for automatic system proxy.");
        }

        var previousSettings = await GetCurrentSettingsAsync(ct).ConfigureAwait(false);

        if (_isGnome)
        {
            // 1. Set HTTP host and port
            await ProcessRunner.RunAsync("gsettings", new[] { "set", "org.gnome.system.proxy.http", "host", profile.Host }, ct: ct).ConfigureAwait(false);
            await ProcessRunner.RunAsync("gsettings", new[] { "set", "org.gnome.system.proxy.http", "port", profile.Port.ToString() }, ct: ct).ConfigureAwait(false);

            // 2. Set HTTPS host and port
            await ProcessRunner.RunAsync("gsettings", new[] { "set", "org.gnome.system.proxy.https", "host", profile.Host }, ct: ct).ConfigureAwait(false);
            await ProcessRunner.RunAsync("gsettings", new[] { "set", "org.gnome.system.proxy.https", "port", profile.Port.ToString() }, ct: ct).ConfigureAwait(false);

            // 3. Set ignore-hosts
            var bypass = profile.BypassRules.Count > 0 
                ? "[" + string.Join(", ", profile.BypassRules.Select(r => $"'{r}'")) + "]"
                : "['localhost', '127.0.0.1', '::1']";
            await ProcessRunner.RunAsync("gsettings", new[] { "set", "org.gnome.system.proxy", "ignore-hosts", bypass }, ct: ct).ConfigureAwait(false);

            // 4. Enable manual mode
            var modeRes = await ProcessRunner.RunAsync("gsettings", new[] { "set", "org.gnome.system.proxy", "mode", "manual" }, ct: ct).ConfigureAwait(false);
            if (modeRes.ExitCode != 0)
            {
                return ProxyApplyResult.Failed($"Failed to set GNOME proxy mode to manual: {modeRes.StandardError}");
            }
        }
        else if (_isKde)
        {
            var kwrite = _kwriteConfigBin ?? "kwriteconfig5";
            var proxyUrl = $"http://{profile.Host}:{profile.Port}";
            await ProcessRunner.RunAsync(kwrite, new[] { "--file", "kioslaverc", "--group", "Proxy Settings", "--key", "ProxyType", "1" }, ct: ct).ConfigureAwait(false);
            await ProcessRunner.RunAsync(kwrite, new[] { "--file", "kioslaverc", "--group", "Proxy Settings", "--key", "httpProxy", proxyUrl }, ct: ct).ConfigureAwait(false);
            await ProcessRunner.RunAsync(kwrite, new[] { "--file", "kioslaverc", "--group", "Proxy Settings", "--key", "httpsProxy", proxyUrl }, ct: ct).ConfigureAwait(false);

            var noProxy = string.Join(",", profile.BypassRules);
            await ProcessRunner.RunAsync(kwrite, new[] { "--file", "kioslaverc", "--group", "Proxy Settings", "--key", "NoProxyFor", noProxy }, ct: ct).ConfigureAwait(false);
        }

        var verified = await VerifyAsync(profile, ct).ConfigureAwait(false);
        if (!verified)
        {
            // Rollback on partial failure (FR-11)
            await RestoreAsync(previousSettings, ct).ConfigureAwait(false);
            return ProxyApplyResult.Failed("Verification failed after applying proxy settings; previous settings were restored.");
        }

        return ProxyApplyResult.Succeeded($"System proxy successfully applied to {profile.Host}:{profile.Port}.", previousSettings);
    }

    public async Task<ProxyRestoreResult> RestoreAsync(SystemProxyConfiguration previousSettings, CancellationToken ct = default)
    {
        if (!IsSupported)
        {
            return ProxyRestoreResult.Failed(UnsupportedReason ?? "Unsupported desktop environment.");
        }

        if (_isGnome)
        {
            if (previousSettings.RawSettings.TryGetValue("gnome:mode", out var prevMode))
            {
                await ProcessRunner.RunAsync("gsettings", new[] { "set", "org.gnome.system.proxy", "mode", prevMode }, ct: ct).ConfigureAwait(false);
            }
            else
            {
                await ProcessRunner.RunAsync("gsettings", new[] { "set", "org.gnome.system.proxy", "mode", "none" }, ct: ct).ConfigureAwait(false);
            }

            if (previousSettings.RawSettings.TryGetValue("gnome:http:host", out var hHost))
                await ProcessRunner.RunAsync("gsettings", new[] { "set", "org.gnome.system.proxy.http", "host", hHost }, ct: ct).ConfigureAwait(false);

            if (previousSettings.RawSettings.TryGetValue("gnome:http:port", out var hPort))
                await ProcessRunner.RunAsync("gsettings", new[] { "set", "org.gnome.system.proxy.http", "port", hPort }, ct: ct).ConfigureAwait(false);

            if (previousSettings.RawSettings.TryGetValue("gnome:ignore-hosts", out var ignores))
                await ProcessRunner.RunAsync("gsettings", new[] { "set", "org.gnome.system.proxy", "ignore-hosts", ignores }, ct: ct).ConfigureAwait(false);

            return ProxyRestoreResult.Succeeded("GNOME proxy settings restored to previous configuration.");
        }

        if (_isKde)
        {
            var kwrite = _kwriteConfigBin ?? "kwriteconfig5";
            var prevType = previousSettings.RawSettings.GetValueOrDefault("kde:ProxyType", "0");
            var prevHttp = previousSettings.RawSettings.GetValueOrDefault("kde:httpProxy", "");
            var prevNoProxy = previousSettings.RawSettings.GetValueOrDefault("kde:NoProxyFor", "");

            await ProcessRunner.RunAsync(kwrite, new[] { "--file", "kioslaverc", "--group", "Proxy Settings", "--key", "ProxyType", prevType }, ct: ct).ConfigureAwait(false);
            await ProcessRunner.RunAsync(kwrite, new[] { "--file", "kioslaverc", "--group", "Proxy Settings", "--key", "httpProxy", prevHttp }, ct: ct).ConfigureAwait(false);
            await ProcessRunner.RunAsync(kwrite, new[] { "--file", "kioslaverc", "--group", "Proxy Settings", "--key", "NoProxyFor", prevNoProxy }, ct: ct).ConfigureAwait(false);

            return ProxyRestoreResult.Succeeded("KDE Plasma proxy settings restored.");
        }

        return ProxyRestoreResult.Failed("Could not restore settings on Linux.");
    }

    public async Task<bool> VerifyAsync(ProxyProfile profile, CancellationToken ct = default)
    {
        var current = await GetCurrentSettingsAsync(ct).ConfigureAwait(false);
        return current.Enabled && 
               string.Equals(current.HttpHost, profile.Host, StringComparison.OrdinalIgnoreCase) && 
               current.HttpPort == profile.Port;
    }
}

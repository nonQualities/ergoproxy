using System.Net;
using System.Text.Json;
using ErgoProxy.Core.Platform;
using ErgoProxy.Core.Tunnel.Native;

namespace ErgoProxy.Core.Tunnel.Platform;

public sealed class WindowsTunnelPlatform : ITunnelPlatform
{
    private static readonly string StateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ergoproxy");
    private static readonly string JournalPath = Path.Combine(StateDirectory, "applied_state.json");

    public bool IsSupported => OperatingSystem.IsWindows();
    public string? UnsupportedReason => IsSupported ? null : "WindowsTunnelPlatform is only supported on Windows.";
    public string PlatformName => "Windows (Wintun + Split Default Routing)";

    public async Task SetupTunnelNetworkAsync(
        TunnelAddressing addressing,
        string? proxyHost,
        int proxyPort,
        CancellationToken ct = default)
    {
        EnsureDirectory();

        // 1. Resolve upstream proxy IP and find physical gateway/interface
        string? proxyIp = null;
        string? physicalGateway = null;
        uint? physicalIfIndex = null;

        if (!string.IsNullOrEmpty(proxyHost))
        {
            try
            {
                var addrs = await System.Net.Dns.GetHostAddressesAsync(proxyHost, ct).ConfigureAwait(false);
                if (addrs.Length > 0)
                {
                    var ipv4 = addrs.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (ipv4 != null)
                    {
                        proxyIp = ipv4.ToString();
                        physicalIfIndex = WindowsNative.GetBestInterfaceFor(ipv4);
                    }
                }
            }
            catch
            {
                // Proxy host resolution may happen later
            }
        }

        // If physical interface index not found via proxy, find it via route to public IP
        if (!physicalIfIndex.HasValue)
        {
            physicalIfIndex = WindowsNative.GetBestInterfaceFor(IPAddress.Parse("8.8.8.8"));
        }

        physicalGateway = await DiscoverDefaultGatewayAsync(ct).ConfigureAwait(false);

        // 2. Configure IP and Subnet mask on Wintun adapter
        var mask = PrefixLengthToSubnetMask(addressing.PrefixLength);
        await RunCommandAsync("netsh", [
            "interface", "ipv4", "set", "address",
            $"name=\"{addressing.InterfaceName}\"",
            "source=static",
            $"address={addressing.InterfaceAddress}",
            $"mask={mask}",
            "gateway=none"
        ], ct).ConfigureAwait(false);

        // Set MTU
        await RunCommandAsync("netsh", [
            "interface", "ipv4", "set", "subinterface",
            $"interface=\"{addressing.InterfaceName}\"",
            $"mtu={addressing.Mtu}",
            "store=active"
        ], ct).ConfigureAwait(false);

        // Set interface metric low so TUN is preferred
        await RunCommandAsync("netsh", [
            "interface", "ipv4", "set", "interface",
            $"interface=\"{addressing.InterfaceName}\"",
            "metric=1"
        ], ct).ConfigureAwait(false);

        // 3. Configure DNS (Fake-IP resolver on addressing.DnsAddress)
        await RunCommandAsync("netsh", [
            "interface", "ipv4", "set", "dnsservers",
            $"name=\"{addressing.InterfaceName}\"",
            "source=static",
            $"address={addressing.DnsAddress}",
            "register=none",
            "validate=no"
        ], ct).ConfigureAwait(false);

        // 4. Install routing table entries
        await CleanStaleRulesAsync(addressing, proxyIp, ct).ConfigureAwait(false);

        // Direct /32 route to upstream proxy via physical gateway (loop prevention)
        if (!string.IsNullOrEmpty(proxyIp) && !string.IsNullOrEmpty(physicalGateway))
        {
            var ifArg = physicalIfIndex.HasValue ? ["if", physicalIfIndex.Value.ToString()] : Array.Empty<string>();
            var routeArgs = new List<string> { "add", proxyIp, "mask", "255.255.255.255", physicalGateway, "metric", "1" };
            routeArgs.AddRange(ifArg);
            await RunCommandAsync("route", routeArgs.ToArray(), ct).ConfigureAwait(false);
        }

        // Tunnel subnet
        await RunCommandAsync("route", ["add", $"{addressing.InterfaceAddress}", "mask", $"{mask}", $"{addressing.InterfaceAddress}", "metric", "1"], ct).ConfigureAwait(false);

        // Dual /1 split default routes to capture all IPv4 traffic into Wintun (LPM)
        await RunCommandAsync("route", ["add", "0.0.0.0", "mask", "128.0.0.0", $"{addressing.InterfaceAddress}", "metric", "1"], ct).ConfigureAwait(false);
        await RunCommandAsync("route", ["add", "128.0.0.0", "mask", "128.0.0.0", $"{addressing.InterfaceAddress}", "metric", "1"], ct).ConfigureAwait(false);

        // 5. Save state journal
        try
        {
            var journal = new Dictionary<string, object?>
            {
                ["interface"] = addressing.InterfaceName,
                ["proxy_host"] = proxyHost,
                ["proxy_ip"] = proxyIp,
                ["physical_gateway"] = physicalGateway,
                ["physical_if_index"] = physicalIfIndex,
                ["created_at"] = DateTimeOffset.UtcNow
            };
            await File.WriteAllTextAsync(JournalPath, JsonSerializer.Serialize(journal), ct).ConfigureAwait(false);
        }
        catch { }
    }

    public async Task TearDownTunnelNetworkAsync(TunnelAddressing addressing, CancellationToken ct = default)
    {
        string? recordedProxyIp = null;
        if (File.Exists(JournalPath))
        {
            try
            {
                var content = await File.ReadAllTextAsync(JournalPath, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("proxy_ip", out var pip) && pip.ValueKind == JsonValueKind.String)
                {
                    recordedProxyIp = pip.GetString();
                }
            }
            catch { }
        }

        await CleanStaleRulesAsync(addressing, recordedProxyIp, ct).ConfigureAwait(false);

        if (File.Exists(JournalPath))
        {
            try { File.Delete(JournalPath); } catch { }
        }
    }

    public async Task CleanStaleStateAsync(TunnelAddressing addressing, CancellationToken ct = default)
    {
        await TearDownTunnelNetworkAsync(addressing, ct).ConfigureAwait(false);
    }

    private static async Task CleanStaleRulesAsync(TunnelAddressing addressing, string? proxyIp, CancellationToken ct)
    {
        // Delete split default routes
        await RunCommandAsync("route", ["delete", "0.0.0.0", "mask", "128.0.0.0"], ct).ConfigureAwait(false);
        await RunCommandAsync("route", ["delete", "128.0.0.0", "mask", "128.0.0.0"], ct).ConfigureAwait(false);

        // Delete proxy direct route if present
        if (!string.IsNullOrEmpty(proxyIp))
        {
            await RunCommandAsync("route", ["delete", proxyIp, "mask", "255.255.255.255"], ct).ConfigureAwait(false);
        }

        // Delete local interface route
        var mask = PrefixLengthToSubnetMask(addressing.PrefixLength);
        await RunCommandAsync("route", ["delete", $"{addressing.InterfaceAddress}", "mask", $"{mask}"], ct).ConfigureAwait(false);
    }

    private static async Task<string?> DiscoverDefaultGatewayAsync(CancellationToken ct)
    {
        var res = await ProcessRunner.RunAsync("route", ["print", "0.0.0.0"], TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
        if (res.ExitCode != 0) return null;

        var lines = res.StandardOutput.Split('\n');
        foreach (var line in lines)
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 && parts[0] == "0.0.0.0" && parts[1] == "0.0.0.0")
            {
                // Destination, Netmask, Gateway, Interface, Metric
                return parts[2];
            }
        }
        return null;
    }

    private static string PrefixLengthToSubnetMask(int prefixLength)
    {
        var mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
        var bytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)mask));
        return new IPAddress(bytes).ToString();
    }

    private static void EnsureDirectory()
    {
        try
        {
            if (!Directory.Exists(StateDirectory))
            {
                Directory.CreateDirectory(StateDirectory);
            }
        }
        catch { }
    }

    private static async Task<bool> RunCommandAsync(string cmd, string[] args, CancellationToken ct)
    {
        var res = await ProcessRunner.RunAsync(cmd, args, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        return res.ExitCode == 0;
    }
}

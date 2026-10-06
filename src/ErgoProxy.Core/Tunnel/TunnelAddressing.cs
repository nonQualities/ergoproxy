using System.Net;
using ErgoProxy.Core.Tunnel.Packets;

namespace ErgoProxy.Core.Tunnel;

/// <summary>
/// Fixed addressing plan of the local tunnel. 198.18.0.0/15 is the RFC 2544 benchmarking range,
/// which is never routed on the public Internet and is the conventional choice for local TUN stacks.
/// </summary>
public sealed class TunnelAddressing
{
    public string InterfaceName { get; init; } = "ergo0";
    public int Mtu { get; init; } = 1500;

    /// <summary>Address of the TUN interface itself; the local relay listener binds here.</summary>
    public IPAddress InterfaceAddress { get; init; } = IPAddress.Parse("198.18.0.1");
    public int PrefixLength { get; init; } = 15;

    /// <summary>Virtual DNS server address announced to the OS resolver.</summary>
    public IPAddress DnsAddress { get; init; } = IPAddress.Parse("198.18.0.2");

    /// <summary>Virtual client address used as the NAT source when re-injecting flows to the relay.</summary>
    public IPAddress NatClientAddress { get; init; } = IPAddress.Parse("198.18.0.3");

    /// <summary>Pool of fake IPs handed out by the DNS server (198.19.0.0/16).</summary>
    public IPAddress FakeIpBase { get; init; } = IPAddress.Parse("198.19.0.0");
    public int FakeIpPrefixLength { get; init; } = 16;

    public IPAddress InterfaceAddressV6 { get; init; } = IPAddress.Parse("fdeb:6f00::1");
    public int PrefixLengthV6 { get; init; } = 64;

    /// <summary>Firewall mark applied to the tunnel's own sockets so they bypass the tunnel.</summary>
    public uint FwMark { get; init; } = 0x4552;
    public int RouteTable { get; init; } = 0x4552;
    public int RulePriority { get; init; } = 5207;

    public uint InterfaceAddressValue => IPv4.ToUInt32(InterfaceAddress);
    public uint DnsAddressValue => IPv4.ToUInt32(DnsAddress);
    public uint NatClientAddressValue => IPv4.ToUInt32(NatClientAddress);

    public bool IsFakeIp(uint address)
    {
        var mask = FakeIpPrefixLength == 0 ? 0u : uint.MaxValue << (32 - FakeIpPrefixLength);
        return (address & mask) == (IPv4.ToUInt32(FakeIpBase) & mask);
    }
}

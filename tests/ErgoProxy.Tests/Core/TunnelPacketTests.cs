using ErgoProxy.Core.Tunnel;
using ErgoProxy.Core.Tunnel.Packets;
using Xunit;

namespace ErgoProxy.Tests.Core;

public class TunnelPacketTests
{
    [Fact]
    public void IPv4_HeaderParsingAndChecksum_RoundTrip()
    {
        var packet = new byte[40];
        var written = IPv4.WriteHeader(packet, 40, IpProtocol.Tcp, 0xC0A80101 /* 192.168.1.1 */, 0xC0A80102 /* 192.168.1.2 */);
        Assert.Equal(20, written);
        Assert.True(IPv4.IsValid(packet));
        Assert.Equal(IpProtocol.Tcp, IPv4.Protocol(packet));
        Assert.Equal(0xC0A80101u, IPv4.Source(packet));
        Assert.Equal(0xC0A80102u, IPv4.Destination(packet));

        // Corrupt and fix checksum
        packet[10] = 0xFF;
        packet[11] = 0xFF;
        Checksum.FixIPv4Header(packet);
        Assert.True(IPv4.IsValid(packet));
    }

    [Fact]
    public void IcmpUnreachable_SynthesizesCorrectTypeAndCode()
    {
        var orig = new byte[40];
        IPv4.WriteHeader(orig, 40, IpProtocol.Udp, 0xC0A80101, 0xC0A80102);
        Ports.SetSource(orig.AsSpan(20), 5000);
        Ports.SetDestination(orig.AsSpan(20), 443);

        var icmp = PacketBuilder.IcmpV4Unreachable(orig, 3 /* port unreachable */);

        Assert.True(IPv4.IsValid(icmp));
        Assert.Equal(IpProtocol.Icmp, IPv4.Protocol(icmp));
        Assert.Equal(0xC0A80102u, IPv4.Source(icmp));
        Assert.Equal(0xC0A80101u, IPv4.Destination(icmp));

        var l4 = icmp.AsSpan(20);
        Assert.Equal(3, l4[0]); // type
        Assert.Equal(3, l4[1]); // code
    }

    [Fact]
    public void NatTable_AllocatesAndRecoversFlow()
    {
        var nat = new NatTable(minPort: 20000, maxPort: 20010);
        var key1 = new FlowKey(0x01010101, 1234, 0x02020202, 80);

        var port1 = nat.Lookup(key1, create: true);
        Assert.NotNull(port1);
        Assert.InRange(port1.Value, 20000, 20010);

        // Same flow returns same port
        var port1Again = nat.Lookup(key1, create: false);
        Assert.Equal(port1, port1Again);

        // Reverse lookup
        var recovered = nat.GetByNatPort(port1.Value);
        Assert.NotNull(recovered);
        Assert.Equal(key1, recovered.Value);
    }
}

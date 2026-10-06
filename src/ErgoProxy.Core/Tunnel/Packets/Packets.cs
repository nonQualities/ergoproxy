using System.Buffers.Binary;
using System.Net;

namespace ErgoProxy.Core.Tunnel.Packets;

public static class IpProtocol
{
    public const byte Icmp = 1;
    public const byte Tcp = 6;
    public const byte Udp = 17;
    public const byte IcmpV6 = 58;
}

/// <summary>RFC 1071 Internet checksum helpers.</summary>
public static class Checksum
{
    public static uint Add(uint sum, ReadOnlySpan<byte> data)
    {
        var i = 0;
        for (; i + 1 < data.Length; i += 2)
        {
            sum += (uint)((data[i] << 8) | data[i + 1]);
        }
        if (i < data.Length)
        {
            sum += (uint)(data[i] << 8);
        }
        return sum;
    }

    public static ushort Finish(uint sum)
    {
        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }
        return (ushort)~sum;
    }

    public static ushort Compute(ReadOnlySpan<byte> data) => Finish(Add(0, data));

    /// <summary>Recomputes the IPv4 header checksum in place.</summary>
    public static void FixIPv4Header(Span<byte> packet)
    {
        var ihl = (packet[0] & 0x0F) * 4;
        packet[10] = 0;
        packet[11] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(packet.Slice(10, 2), Compute(packet[..ihl]));
    }

    /// <summary>Recomputes a TCP/UDP checksum (IPv4) in place. checksumOffset is relative to the L4 header.</summary>
    public static void FixIPv4Transport(Span<byte> packet, int l4Offset, int l4Length, byte protocol, int checksumOffset)
    {
        var l4 = packet.Slice(l4Offset, l4Length);
        l4[checksumOffset] = 0;
        l4[checksumOffset + 1] = 0;
        uint sum = 0;
        sum = Add(sum, packet.Slice(12, 8)); // src + dst
        sum += protocol;
        sum += (uint)l4Length;
        sum = Add(sum, l4);
        var value = Finish(sum);
        if (protocol == IpProtocol.Udp && value == 0) value = 0xFFFF;
        BinaryPrimitives.WriteUInt16BigEndian(l4.Slice(checksumOffset, 2), value);
    }

    /// <summary>Recomputes a TCP/UDP/ICMPv6 checksum (IPv6, no extension headers) in place.</summary>
    public static void FixIPv6Transport(Span<byte> packet, int l4Length, byte protocol, int checksumOffset)
    {
        var l4 = packet.Slice(40, l4Length);
        l4[checksumOffset] = 0;
        l4[checksumOffset + 1] = 0;
        uint sum = 0;
        sum = Add(sum, packet.Slice(8, 32)); // src + dst
        sum += (uint)(l4Length >> 16);
        sum += (uint)(l4Length & 0xFFFF);
        sum += protocol;
        sum = Add(sum, l4);
        var value = Finish(sum);
        if (protocol == IpProtocol.Udp && value == 0) value = 0xFFFF;
        BinaryPrimitives.WriteUInt16BigEndian(l4.Slice(checksumOffset, 2), value);
    }
}

/// <summary>Zero-copy accessors for IPv4 packets.</summary>
public static class IPv4
{
    public static bool IsValid(ReadOnlySpan<byte> p)
    {
        if (p.Length < 20 || (p[0] >> 4) != 4) return false;
        var ihl = (p[0] & 0x0F) * 4;
        var total = BinaryPrimitives.ReadUInt16BigEndian(p[2..]);
        return ihl >= 20 && total >= ihl && total <= p.Length;
    }

    public static int HeaderLength(ReadOnlySpan<byte> p) => (p[0] & 0x0F) * 4;
    public static int TotalLength(ReadOnlySpan<byte> p) => BinaryPrimitives.ReadUInt16BigEndian(p[2..]);
    public static byte Protocol(ReadOnlySpan<byte> p) => p[9];
    public static bool IsFragment(ReadOnlySpan<byte> p) => (BinaryPrimitives.ReadUInt16BigEndian(p[6..]) & 0x3FFF) != 0;

    public static uint Source(ReadOnlySpan<byte> p) => BinaryPrimitives.ReadUInt32BigEndian(p[12..]);
    public static uint Destination(ReadOnlySpan<byte> p) => BinaryPrimitives.ReadUInt32BigEndian(p[16..]);
    public static void SetSource(Span<byte> p, uint addr) => BinaryPrimitives.WriteUInt32BigEndian(p[12..], addr);
    public static void SetDestination(Span<byte> p, uint addr) => BinaryPrimitives.WriteUInt32BigEndian(p[16..], addr);

    public static uint ToUInt32(IPAddress address)
    {
        Span<byte> b = stackalloc byte[4];
        if (!address.TryWriteBytes(b, out var written) || written != 4)
            throw new ArgumentException("Not an IPv4 address", nameof(address));
        return BinaryPrimitives.ReadUInt32BigEndian(b);
    }

    public static IPAddress ToAddress(uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        return new IPAddress(b);
    }

    public static string Format(uint value) => ToAddress(value).ToString();

    /// <summary>Writes a basic 20-byte IPv4 header (no options) and returns its length.</summary>
    public static int WriteHeader(Span<byte> p, int totalLength, byte protocol, uint src, uint dst)
    {
        p[0] = 0x45;
        p[1] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(p[2..], (ushort)totalLength);
        BinaryPrimitives.WriteUInt16BigEndian(p[4..], (ushort)Random.Shared.Next(0, 0xFFFF));
        BinaryPrimitives.WriteUInt16BigEndian(p[6..], 0x4000); // DF
        p[8] = 64;
        p[9] = protocol;
        SetSource(p, src);
        SetDestination(p, dst);
        Checksum.FixIPv4Header(p);
        return 20;
    }
}

/// <summary>Zero-copy accessors for IPv6 packets (without extension headers).</summary>
public static class IPv6
{
    public const int HeaderLength = 40;

    public static bool IsValid(ReadOnlySpan<byte> p)
    {
        if (p.Length < HeaderLength || (p[0] >> 4) != 6) return false;
        var payload = BinaryPrimitives.ReadUInt16BigEndian(p[4..]);
        return HeaderLength + payload <= p.Length;
    }

    public static int PayloadLength(ReadOnlySpan<byte> p) => BinaryPrimitives.ReadUInt16BigEndian(p[4..]);
    public static byte NextHeader(ReadOnlySpan<byte> p) => p[6];

    public static void WriteHeader(Span<byte> p, int payloadLength, byte nextHeader, ReadOnlySpan<byte> src, ReadOnlySpan<byte> dst)
    {
        p[0] = 0x60;
        p[1] = 0;
        p[2] = 0;
        p[3] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(p[4..], (ushort)payloadLength);
        p[6] = nextHeader;
        p[7] = 64;
        src.CopyTo(p.Slice(8, 16));
        dst.CopyTo(p.Slice(24, 16));
    }
}

public static class Ports
{
    public static ushort Source(ReadOnlySpan<byte> l4) => BinaryPrimitives.ReadUInt16BigEndian(l4);
    public static ushort Destination(ReadOnlySpan<byte> l4) => BinaryPrimitives.ReadUInt16BigEndian(l4[2..]);
    public static void SetSource(Span<byte> l4, ushort port) => BinaryPrimitives.WriteUInt16BigEndian(l4, port);
    public static void SetDestination(Span<byte> l4, ushort port) => BinaryPrimitives.WriteUInt16BigEndian(l4[2..], port);
}

public static class TcpFlags
{
    public const byte Fin = 0x01;
    public const byte Syn = 0x02;
    public const byte Rst = 0x04;
    public const byte Ack = 0x10;

    public static byte Get(ReadOnlySpan<byte> tcp) => tcp[13];
}

/// <summary>Builders for packets the engine synthesises (DNS replies, ICMP rejections).</summary>
public static class PacketBuilder
{
    public static byte[] UdpV4(uint src, ushort srcPort, uint dst, ushort dstPort, ReadOnlySpan<byte> payload)
    {
        var udpLen = 8 + payload.Length;
        var packet = new byte[20 + udpLen];
        IPv4.WriteHeader(packet, packet.Length, IpProtocol.Udp, src, dst);
        var udp = packet.AsSpan(20);
        Ports.SetSource(udp, srcPort);
        Ports.SetDestination(udp, dstPort);
        BinaryPrimitives.WriteUInt16BigEndian(udp[4..], (ushort)udpLen);
        payload.CopyTo(udp[8..]);
        Checksum.FixIPv4Transport(packet, 20, udpLen, IpProtocol.Udp, 6);
        return packet;
    }

    public static byte[] UdpV6(ReadOnlySpan<byte> src, ushort srcPort, ReadOnlySpan<byte> dst, ushort dstPort, ReadOnlySpan<byte> payload)
    {
        var udpLen = 8 + payload.Length;
        var packet = new byte[IPv6.HeaderLength + udpLen];
        IPv6.WriteHeader(packet, udpLen, IpProtocol.Udp, src, dst);
        var udp = packet.AsSpan(IPv6.HeaderLength);
        Ports.SetSource(udp, srcPort);
        Ports.SetDestination(udp, dstPort);
        BinaryPrimitives.WriteUInt16BigEndian(udp[4..], (ushort)udpLen);
        payload.CopyTo(udp[8..]);
        Checksum.FixIPv6Transport(packet, udpLen, IpProtocol.Udp, 6);
        return packet;
    }

    /// <summary>ICMPv4 destination unreachable (type 3) in response to <paramref name="original"/>.</summary>
    public static byte[] IcmpV4Unreachable(ReadOnlySpan<byte> original, byte code)
    {
        var ihl = IPv4.HeaderLength(original);
        var quoted = Math.Min(original.Length, ihl + 8);
        var icmpLen = 8 + quoted;
        var packet = new byte[20 + icmpLen];
        IPv4.WriteHeader(packet, packet.Length, IpProtocol.Icmp, IPv4.Destination(original), IPv4.Source(original));
        var icmp = packet.AsSpan(20);
        icmp[0] = 3;
        icmp[1] = code;
        original[..quoted].CopyTo(icmp[8..]);
        BinaryPrimitives.WriteUInt16BigEndian(icmp[2..], Checksum.Compute(icmp));
        return packet;
    }

    /// <summary>ICMPv6 destination unreachable (type 1) in response to <paramref name="original"/>.</summary>
    public static byte[] IcmpV6Unreachable(ReadOnlySpan<byte> original, byte code)
    {
        var quoted = Math.Min(original.Length, 1280 - IPv6.HeaderLength - 8);
        var icmpLen = 8 + quoted;
        var packet = new byte[IPv6.HeaderLength + icmpLen];
        IPv6.WriteHeader(packet, icmpLen, IpProtocol.IcmpV6, original.Slice(24, 16), original.Slice(8, 16));
        var icmp = packet.AsSpan(IPv6.HeaderLength);
        icmp[0] = 1;
        icmp[1] = code;
        original[..quoted].CopyTo(icmp[8..]);
        Checksum.FixIPv6Transport(packet, icmpLen, IpProtocol.IcmpV6, 2);
        return packet;
    }
}

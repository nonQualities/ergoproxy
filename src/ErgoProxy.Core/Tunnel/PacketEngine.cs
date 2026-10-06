using System.Buffers.Binary;
using ErgoProxy.Core.Models;
using ErgoProxy.Core.Tunnel.Dns;
using ErgoProxy.Core.Tunnel.Packets;

namespace ErgoProxy.Core.Tunnel;

public sealed class PacketEngine : IDisposable
{
    private readonly IPacketDevice _device;
    private readonly TunnelAddressing _addressing;
    private readonly NatTable _natTable;
    private readonly FakeDnsServer _dnsServer;
    private readonly TunnelStats _stats;
    private readonly ushort _relayPort;

    private readonly CancellationTokenSource _cts = new();
    private Task? _readLoopTask;
    private Task? _sweepTask;

    public PacketEngine(
        IPacketDevice device,
        TunnelAddressing addressing,
        NatTable natTable,
        FakeDnsServer dnsServer,
        TunnelStats stats,
        int relayPort)
    {
        _device = device;
        _addressing = addressing;
        _natTable = natTable;
        _dnsServer = dnsServer;
        _stats = stats;
        _relayPort = (ushort)relayPort;
    }

    public void Start()
    {
        _readLoopTask = Task.Run(ReadLoop);
        _sweepTask = Task.Run(SweepLoop);
    }

    private void ReadLoop()
    {
        var buffer = new byte[65536];

        while (!_cts.Token.IsCancellationRequested)
        {
            var len = _device.Read(buffer, 100);
            if (len < 0) break; // Device closed
            if (len == 0) continue; // Timeout, check cancellation

            Interlocked.Increment(ref _stats.PacketsIn);

            try
            {
                ProcessPacket(buffer.AsSpan(0, len));
            }
            catch
            {
                // Packet processing error, drop packet and continue
            }
        }
    }

    public void ProcessPacket(Span<byte> packet)
    {
        if (packet.Length < 20) return;

        var version = packet[0] >> 4;

        if (version == 4)
        {
            ProcessIPv4(packet);
        }
        else if (version == 6)
        {
            ProcessIPv6(packet);
        }
    }

    private void ProcessIPv4(Span<byte> packet)
    {
        if (!IPv4.IsValid(packet) || IPv4.IsFragment(packet)) return;

        var ihl = IPv4.HeaderLength(packet);
        var protocol = IPv4.Protocol(packet);
        var l4 = packet[ihl..];

        // 1. DNS (UDP to port 53)
        if (protocol == IpProtocol.Udp && l4.Length >= 8)
        {
            var dstPort = Ports.Destination(l4);
            if (dstPort == 53)
            {
                var srcPort = Ports.Source(l4);
                var srcIp = IPv4.Source(packet);
                var dstIp = IPv4.Destination(packet);
                var dnsPayload = l4[8..];

                var dnsResponse = _dnsServer.HandleQuery(dnsPayload);
                if (dnsResponse != null)
                {
                    Interlocked.Increment(ref _stats.DnsQueries);
                    var replyPacket = PacketBuilder.UdpV4(
                        src: dstIp,
                        srcPort: 53,
                        dst: srcIp,
                        dstPort: srcPort,
                        payload: dnsResponse);

                    _device.Write(replyPacket);
                    Interlocked.Increment(ref _stats.PacketsOut);
                }
                return;
            }

            // Other UDP traffic: reject with ICMP port unreachable
            var icmp = PacketBuilder.IcmpV4Unreachable(packet, 3);
            _device.Write(icmp);
            Interlocked.Increment(ref _stats.UdpRejected);
            Interlocked.Increment(ref _stats.PacketsOut);
            return;
        }

        // 2. TCP Interception and Demuxing
        if (protocol == IpProtocol.Tcp && l4.Length >= 20)
        {
            var srcIp = IPv4.Source(packet);
            var dstIp = IPv4.Destination(packet);
            var srcPort = Ports.Source(l4);
            var dstPort = Ports.Destination(l4);

            // Is this a packet coming back FROM our local relay listener?
            if (srcIp == _addressing.InterfaceAddressValue && srcPort == _relayPort)
            {
                // Destination should be our NAT client address 198.18.0.3, and dstPort is the natPort
                var natPort = dstPort;
                var flowKey = _natTable.GetByNatPort(natPort, touch: true);
                if (flowKey.HasValue)
                {
                    // Demux back to original flow
                    IPv4.SetSource(packet, flowKey.Value.DestinationIp);
                    IPv4.SetDestination(packet, flowKey.Value.SourceIp);
                    Ports.SetSource(l4, flowKey.Value.DestinationPort);
                    Ports.SetDestination(l4, flowKey.Value.SourcePort);

                    Checksum.FixIPv4Header(packet);
                    Checksum.FixIPv4Transport(packet, ihl, l4.Length, IpProtocol.Tcp, 16);

                    _device.Write(packet);
                    Interlocked.Increment(ref _stats.PacketsOut);
                }
                return;
            }

            // Outbound traffic from an application to intercept
            // Ignore if it's already targeted at the interface address on relay port
            if (dstIp == _addressing.InterfaceAddressValue && dstPort == _relayPort)
            {
                return;
            }

            var tcpFlags = TcpFlags.Get(l4);
            var isSyn = (tcpFlags & TcpFlags.Syn) != 0;

            var key = new FlowKey(srcIp, srcPort, dstIp, dstPort);
            var natPortAllocated = _natTable.Lookup(key, create: isSyn || _natTable.Count < 5000);

            if (natPortAllocated.HasValue)
            {
                // Rewrite packet towards local relay listener
                IPv4.SetSource(packet, _addressing.NatClientAddressValue);
                IPv4.SetDestination(packet, _addressing.InterfaceAddressValue);
                Ports.SetSource(l4, natPortAllocated.Value);
                Ports.SetDestination(l4, _relayPort);

                Checksum.FixIPv4Header(packet);
                Checksum.FixIPv4Transport(packet, ihl, l4.Length, IpProtocol.Tcp, 16);

                _device.Write(packet);
                Interlocked.Increment(ref _stats.PacketsOut);
            }
            return;
        }

        // Other IPv4 protocols (e.g. ICMP ping) -> drop or reject
        if (protocol != IpProtocol.Icmp)
        {
            var icmp = PacketBuilder.IcmpV4Unreachable(packet, 2); // Protocol unreachable
            _device.Write(icmp);
            Interlocked.Increment(ref _stats.PacketsOut);
        }
    }

    private void ProcessIPv6(Span<byte> packet)
    {
        if (!IPv6.IsValid(packet)) return;

        // Reject with ICMPv6 Destination Unreachable (code 1 = Administratively Prohibited)
        var icmp = PacketBuilder.IcmpV6Unreachable(packet, 1);
        _device.Write(icmp);
        Interlocked.Increment(ref _stats.Ipv6Rejected);
        Interlocked.Increment(ref _stats.PacketsOut);
    }

    private async Task SweepLoop()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), _cts.Token).ConfigureAwait(false);
                _natTable.Sweep();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore sweep errors
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _readLoopTask?.Wait(500); } catch { }
        try { _sweepTask?.Wait(500); } catch { }
        _cts.Dispose();
    }
}

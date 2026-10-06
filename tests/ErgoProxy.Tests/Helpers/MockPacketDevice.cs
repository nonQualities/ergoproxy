using System.Collections.Concurrent;
using ErgoProxy.Core.Tunnel;

namespace ErgoProxy.Tests.Helpers;

public sealed class MockPacketDevice : IPacketDevice
{
    private readonly BlockingCollection<byte[]> _inbound = new();
    public ConcurrentQueue<byte[]> Outbound { get; } = new();
    private bool _disposed;

    public string Name => "mock0";

    public void InjectInbound(byte[] packet)
    {
        if (!_disposed)
        {
            _inbound.Add(packet);
        }
    }

    public int Read(Span<byte> buffer, int timeoutMs)
    {
        if (_disposed) return -1;

        try
        {
            if (_inbound.TryTake(out var packet, timeoutMs))
            {
                packet.AsSpan().CopyTo(buffer);
                return packet.Length;
            }
            return 0; // timeout
        }
        catch
        {
            return -1;
        }
    }

    public void Write(ReadOnlySpan<byte> packet)
    {
        if (!_disposed)
        {
            Outbound.Enqueue(packet.ToArray());
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _inbound.CompleteAdding();
        _inbound.Dispose();
    }
}

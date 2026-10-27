using System.Runtime.InteropServices;
using ErgoProxy.Core.Tunnel.Native;

namespace ErgoProxy.Core.Tunnel;

/// <summary>
/// Windows L3 packet device implemented via WireGuard's Wintun driver.
/// Provides high-throughput, ring-buffer packet I/O without user-space TCP stack overhead.
/// </summary>
public sealed unsafe class WindowsTunDevice : IPacketDevice
{
    private nint _adapter;
    private nint _session;
    private nint _readWaitEvent;
    private int _isDisposed;

    public string Name { get; }

    private WindowsTunDevice(nint adapter, nint session, nint readWaitEvent, string name)
    {
        _adapter = adapter;
        _session = session;
        _readWaitEvent = readWaitEvent;
        Name = name;
    }

    public static WindowsTunDevice Open(string name)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WindowsTunDevice is only supported on Windows.");

        var guid = Guid.NewGuid();
        var adapter = WintunDll.WintunCreateAdapter(name, "ErgoProxy", in guid);
        if (adapter == 0)
        {
            adapter = WintunDll.WintunOpenAdapter(name);
        }

        if (adapter == 0)
        {
            var err = Marshal.GetLastPInvokeError();
            throw new IOException($"Failed to create or open Wintun adapter '{name}' (Win32 error {err}). Ensure wintun.dll is present and process is running as Administrator.");
        }

        var session = WintunDll.WintunStartSession(adapter, WintunDll.WINTUN_DEFAULT_RING_CAPACITY);
        if (session == 0)
        {
            var err = Marshal.GetLastPInvokeError();
            WintunDll.WintunCloseAdapter(adapter);
            throw new IOException($"WintunStartSession failed for '{name}' (Win32 error {err}).");
        }

        var waitEvent = WintunDll.WintunGetReadWaitEvent(session);
        return new WindowsTunDevice(adapter, session, waitEvent, name);
    }

    public int Read(Span<byte> buffer, int timeoutMs)
    {
        if (Volatile.Read(ref _isDisposed) != 0) return -1;

        var waitRes = WindowsNative.WaitForSingleObject(_readWaitEvent, (uint)timeoutMs);
        if (waitRes == WindowsNative.WAIT_TIMEOUT) return 0;
        if (waitRes != WindowsNative.WAIT_OBJECT_0) return -1;

        if (Volatile.Read(ref _isDisposed) != 0) return -1;

        var packetPtr = WintunDll.WintunReceivePacket(_session, out var packetSize);
        if (packetPtr == null) return 0;

        try
        {
            if (packetSize > buffer.Length)
            {
                return 0; // Buffer too small for packet
            }

            new ReadOnlySpan<byte>(packetPtr, (int)packetSize).CopyTo(buffer);
            return (int)packetSize;
        }
        finally
        {
            WintunDll.WintunReleaseReceivePacket(_session, packetPtr);
        }
    }

    public void Write(ReadOnlySpan<byte> packet)
    {
        if (Volatile.Read(ref _isDisposed) != 0) return;

        var sendPtr = WintunDll.WintunAllocateSendPacket(_session, (uint)packet.Length);
        if (sendPtr == null) return; // Ring buffer full or packet dropped

        packet.CopyTo(new Span<byte>(sendPtr, packet.Length));
        WintunDll.WintunSendPacket(_session, sendPtr);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

        if (_session != 0)
        {
            WintunDll.WintunEndSession(_session);
            _session = 0;
        }

        if (_adapter != 0)
        {
            WintunDll.WintunCloseAdapter(_adapter);
            _adapter = 0;
        }

        _readWaitEvent = 0;
    }
}

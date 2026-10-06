using System.Runtime.InteropServices;
using System.Text;
using ErgoProxy.Core.Tunnel.Native;

namespace ErgoProxy.Core.Tunnel;

/// <summary>Abstraction over a layer-3 packet device so the engine can be tested without a real TUN.</summary>
public interface IPacketDevice : IDisposable
{
    string Name { get; }
    /// <summary>Blocks until a packet is available, the timeout elapses (returns 0) or the device is closed (returns -1).</summary>
    int Read(Span<byte> buffer, int timeoutMs);
    void Write(ReadOnlySpan<byte> packet);
}

/// <summary>Linux TUN device (IFF_TUN | IFF_NO_PI). The interface disappears automatically when closed.</summary>
public sealed unsafe class LinuxTunDevice : IPacketDevice
{
    private int _fd;

    public string Name { get; }

    private LinuxTunDevice(int fd, string name)
    {
        _fd = fd;
        Name = name;
    }

    public static LinuxTunDevice Open(string name)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("TUN devices are only implemented for Linux.");
        if (Encoding.ASCII.GetByteCount(name) >= 16)
            throw new ArgumentException("Interface name must be shorter than 16 characters.", nameof(name));

        var fd = LinuxNative.Open("/dev/net/tun", LinuxNative.O_RDWR | LinuxNative.O_CLOEXEC);
        if (fd < 0)
        {
            var errno = Marshal.GetLastPInvokeError();
            throw new IOException($"Cannot open /dev/net/tun (errno {errno}). Is the 'tun' kernel module available?");
        }

        var ifr = new LinuxNative.IfReq { Flags = LinuxNative.IFF_TUN | LinuxNative.IFF_NO_PI };
        var bytes = Encoding.ASCII.GetBytes(name);
        for (var i = 0; i < bytes.Length; i++) ifr.Name[i] = bytes[i];

        if (LinuxNative.Ioctl(fd, LinuxNative.TUNSETIFF, ref ifr) < 0)
        {
            var errno = Marshal.GetLastPInvokeError();
            LinuxNative.Close(fd);
            var hint = errno == 1 ? " (operation not permitted: root/CAP_NET_ADMIN required)" :
                       errno == 16 ? " (device busy: is another tunnel already running?)" : string.Empty;
            throw new IOException($"TUNSETIFF failed for '{name}' (errno {errno}){hint}.");
        }

        return new LinuxTunDevice(fd, name);
    }

    public int Read(Span<byte> buffer, int timeoutMs)
    {
        var fd = Volatile.Read(ref _fd);
        if (fd < 0) return -1;

        var pfd = new LinuxNative.PollFd { Fd = fd, Events = LinuxNative.POLLIN };
        var ready = LinuxNative.Poll(&pfd, 1, timeoutMs);
        if (ready == 0) return 0;
        if (ready < 0)
        {
            var errno = Marshal.GetLastPInvokeError();
            return errno == 4 /* EINTR */ ? 0 : -1;
        }
        if ((pfd.REvents & ~LinuxNative.POLLIN) != 0 && (pfd.REvents & LinuxNative.POLLIN) == 0) return -1;

        fixed (byte* p = buffer)
        {
            var n = LinuxNative.Read(fd, p, buffer.Length);
            if (n < 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                return errno is 4 or 11 ? 0 : -1;
            }
            return (int)n;
        }
    }

    public void Write(ReadOnlySpan<byte> packet)
    {
        var fd = Volatile.Read(ref _fd);
        if (fd < 0) return;
        fixed (byte* p = packet)
        {
            LinuxNative.Write(fd, p, packet.Length);
        }
    }

    public void Dispose()
    {
        var fd = Interlocked.Exchange(ref _fd, -1);
        if (fd >= 0) LinuxNative.Close(fd);
    }
}

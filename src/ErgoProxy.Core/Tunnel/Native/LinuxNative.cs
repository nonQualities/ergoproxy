using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace ErgoProxy.Core.Tunnel.Native;

/// <summary>
/// Minimal libc bindings needed for the Linux tunnel (TUN device, process detachment, socket marks).
/// </summary>
internal static unsafe partial class LinuxNative
{
    public const int O_RDWR = 0x0002;
    public const int O_CLOEXEC = 0x80000;
    public const ulong TUNSETIFF = 0x400454ca;
    public const short IFF_TUN = 0x0001;
    public const short IFF_NO_PI = 0x1000;
    public const short POLLIN = 0x0001;

    public const int SOL_SOCKET = 1;
    public const int SO_PEERCRED = 17;
    public const int SO_MARK = 36;

    [StructLayout(LayoutKind.Sequential)]
    public struct IfReq
    {
        public fixed byte Name[16];
        public short Flags;
        public fixed byte Padding[22];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int Fd;
        public short Events;
        public short REvents;
    }

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    public static partial int Close(int fd);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    public static partial int Ioctl(int fd, ulong request, ref IfReq ifr);

    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    public static partial nint Read(int fd, byte* buf, nint count);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    public static partial nint Write(int fd, byte* buf, nint count);

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    public static partial int Poll(PollFd* fds, ulong nfds, int timeoutMs);

    [LibraryImport("libc", EntryPoint = "setsid", SetLastError = true)]
    public static partial int SetSid();

    [LibraryImport("libc", EntryPoint = "geteuid")]
    public static partial uint GetEuid();

    [LibraryImport("libc", EntryPoint = "getuid")]
    public static partial uint GetUid();

    [LibraryImport("libc", EntryPoint = "dup2", SetLastError = true)]
    public static partial int Dup2(int oldFd, int newFd);

    [LibraryImport("libc", EntryPoint = "chown", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Chown(string path, uint owner, uint group);

    public static bool IsLinux => OperatingSystem.IsLinux();

    public static bool IsRoot => IsLinux && GetEuid() == 0;

    /// <summary>Marks a socket so that policy routing sends it around the tunnel (loop prevention).</summary>
    public static void SetMark(Socket socket, uint mark)
    {
        if (mark == 0 || !IsLinux) return;
        try
        {
            Span<byte> value = stackalloc byte[4];
            BitConverter.TryWriteBytes(value, mark);
            socket.SetRawSocketOption(SOL_SOCKET, SO_MARK, value);
        }
        catch (SocketException)
        {
            // Unprivileged execution (e.g. tests running without CAP_NET_ADMIN)
        }
    }

    /// <summary>Returns the uid of the peer of a connected Unix domain socket.</summary>
    public static uint? GetPeerUid(Socket socket)
    {
        if (!IsLinux) return null;
        Span<byte> cred = stackalloc byte[12]; // struct ucred { pid_t pid; uid_t uid; gid_t gid; }
        var len = socket.GetRawSocketOption(SOL_SOCKET, SO_PEERCRED, cred);
        if (len < 12) return null;
        return BitConverter.ToUInt32(cred[4..8]);
    }

    /// <summary>Re-points stdin/stdout/stderr at /dev/null so a detached daemon never touches the terminal.</summary>
    public static void DetachStdio()
    {
        var fd = Open("/dev/null", O_RDWR);
        if (fd < 0) return;
        Dup2(fd, 0);
        Dup2(fd, 1);
        Dup2(fd, 2);
        if (fd > 2) Close(fd);
    }
}

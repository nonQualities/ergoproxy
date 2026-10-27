using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace ErgoProxy.Core.Tunnel.Native;

/// <summary>
/// Minimal Win32 and Winsock interop for the Windows tunnel platform (interface pinning, loop prevention).
/// </summary>
internal static unsafe partial class WindowsNative
{
    public const int IPPROTO_IP = 0;
    public const int IPPROTO_IPV6 = 41;
    public const int IP_UNICAST_IF = 31;
    public const int IPV6_UNICAST_IF = 31;

    public const uint INFINITE = 0xFFFFFFFF;
    public const uint WAIT_OBJECT_0 = 0x00000000;
    public const uint WAIT_TIMEOUT = 0x00000102;
    public const uint WAIT_FAILED = 0xFFFFFFFF;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [LibraryImport("iphlpapi.dll", EntryPoint = "GetBestInterface", SetLastError = true)]
    public static partial uint GetBestInterface(uint dwDestAddr, out uint pdwBestIfIndex);

    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>
    /// Forces an outgoing socket to egress through a specific physical network interface index.
    /// This bypasses the default routing table and prevents tunnel routing loops on Windows.
    /// </summary>
    public static void SetUnicastInterface(Socket socket, uint ifIndex)
    {
        if (ifIndex == 0 || !IsWindows) return;

        try
        {
            // Windows expects the interface index in network byte order
            uint netOrder = (uint)IPAddress.HostToNetworkOrder((int)ifIndex);
            Span<byte> val = stackalloc byte[4];
            BitConverter.TryWriteBytes(val, netOrder);

            if (socket.AddressFamily == AddressFamily.InterNetwork)
            {
                socket.SetRawSocketOption(IPPROTO_IP, IP_UNICAST_IF, val);
            }
            else if (socket.AddressFamily == AddressFamily.InterNetworkV6)
            {
                socket.SetRawSocketOption(IPPROTO_IPV6, IPV6_UNICAST_IF, val);
            }
        }
        catch (SocketException)
        {
            // Non-fatal if interface indexing is not accepted on this socket type
        }
    }

    /// <summary>
    /// Gets the best interface index to reach a given IPv4 destination.
    /// </summary>
    public static uint? GetBestInterfaceFor(IPAddress destination)
    {
        if (!IsWindows || destination.AddressFamily != AddressFamily.InterNetwork)
            return null;

        var bytes = destination.GetAddressBytes();
        var destAddr = BitConverter.ToUInt32(bytes); // host byte order for GetBestInterface
        if (GetBestInterface(destAddr, out var bestIfIndex) == 0)
        {
            return bestIfIndex;
        }

        return null;
    }
}

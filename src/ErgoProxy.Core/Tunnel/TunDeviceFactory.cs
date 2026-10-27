namespace ErgoProxy.Core.Tunnel;

public static class TunDeviceFactory
{
    public static IPacketDevice Create(string interfaceName)
    {
        if (OperatingSystem.IsLinux())
        {
            return LinuxTunDevice.Open(interfaceName);
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsTunDevice.Open(interfaceName);
        }

        throw new PlatformNotSupportedException($"TUN devices are not supported on {Environment.OSVersion.Platform}.");
    }
}

using System.Runtime.InteropServices;

namespace ErgoProxy.Core.Platform;

public static class PlatformAdapterFactory
{
    public static IPlatformAdapter CreateDefault()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var linux = new LinuxPlatformAdapter();
            return linux.IsSupported ? linux : new UnsupportedPlatformAdapter();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsPlatformAdapter();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOSPlatformAdapter();
        }

        return new UnsupportedPlatformAdapter();
    }
}

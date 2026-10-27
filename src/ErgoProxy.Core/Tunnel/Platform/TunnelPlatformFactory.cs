namespace ErgoProxy.Core.Tunnel.Platform;

public static class TunnelPlatformFactory
{
    public static ITunnelPlatform Create()
    {
        if (OperatingSystem.IsLinux())
        {
            return new LinuxTunnelPlatform();
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsTunnelPlatform();
        }

        return new UnsupportedTunnelPlatform();
    }

    private sealed class UnsupportedTunnelPlatform : ITunnelPlatform
    {
        public bool IsSupported => false;
        public string? UnsupportedReason => $"Operating system '{Environment.OSVersion}' is not supported for device-wide tunneling.";
        public string PlatformName => "Unsupported";

        public Task SetupTunnelNetworkAsync(TunnelAddressing addressing, string? proxyHost, int proxyPort, CancellationToken ct = default)
            => throw new PlatformNotSupportedException(UnsupportedReason);

        public Task TearDownTunnelNetworkAsync(TunnelAddressing addressing, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task CleanStaleStateAsync(TunnelAddressing addressing, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}

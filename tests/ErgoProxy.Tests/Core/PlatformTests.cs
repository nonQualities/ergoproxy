using ErgoProxy.Core.Daemon;
using ErgoProxy.Core.Tunnel;
using ErgoProxy.Core.Tunnel.Native;
using ErgoProxy.Core.Tunnel.Platform;
using Xunit;

namespace ErgoProxy.Tests.Core;

public class PlatformTests
{
    [Fact]
    public void TunnelPlatformFactory_ReturnsCorrectPlatformForOS()
    {
        var platform = TunnelPlatformFactory.Create();
        Assert.NotNull(platform);

        if (OperatingSystem.IsLinux())
        {
            Assert.IsType<LinuxTunnelPlatform>(platform);
            Assert.True(platform.IsSupported);
            Assert.Contains("Linux", platform.PlatformName);
        }
        else if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsTunnelPlatform>(platform);
            Assert.True(platform.IsSupported);
            Assert.Contains("Windows", platform.PlatformName);
        }
    }

    [Fact]
    public void WindowsTunnelPlatform_Properties_AreConsistent()
    {
        var platform = new WindowsTunnelPlatform();
        Assert.Equal(OperatingSystem.IsWindows(), platform.IsSupported);
        Assert.Contains("Windows", platform.PlatformName);

        if (OperatingSystem.IsWindows())
        {
            Assert.Null(platform.UnsupportedReason);
        }
        else
        {
            Assert.NotNull(platform.UnsupportedReason);
        }
    }

    [Fact]
    public void DaemonClient_DefaultEndpoint_IsPlatformAppropriate()
    {
        var endpoint = DaemonClient.DefaultEndpoint;
        Assert.False(string.IsNullOrWhiteSpace(endpoint));

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(DaemonClient.DefaultWindowsPipeName, endpoint);
        }
        else
        {
            Assert.Equal(DaemonClient.DefaultLinuxSocketPath, endpoint);
        }
    }

    [Fact]
    public void WindowsNative_SetUnicastInterface_HandlesGracefully()
    {
        using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        // Should not throw even when called on arbitrary interface indices or non-Windows OS
        WindowsNative.SetUnicastInterface(socket, 0);
        WindowsNative.SetUnicastInterface(socket, 1);
    }
}

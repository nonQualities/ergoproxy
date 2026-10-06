using ErgoProxy.Core.Models;
using ErgoProxy.Core.Network;
using ErgoProxy.Tests.Helpers;
using Xunit;

namespace ErgoProxy.Tests.Core;

public class ProxyTesterTests
{
    private readonly ProxyTester _tester = new();

    [Fact]
    public async Task TestAsync_NonExistentHost_ReturnsDnsResolutionFailed()
    {
        var profile = new ProxyProfile
        {
            Id = "p-dns-fail",
            Name = "Bad DNS",
            Host = "this-domain-surely-does-not-exist-123456789.invalid",
            Port = 8080
        };

        var options = new ProxyTestOptions
        {
            Timeout = TimeSpan.FromSeconds(2),
            TestHttpsConnectTunnel = false
        };

        var result = await _tester.TestAsync(profile, null, options);
        Assert.False(result.IsSuccess);
        Assert.Equal(ProxyTestStage.DnsResolution, result.Stage);
        Assert.True(
            result.ErrorCode is ProxyTestErrorCode.DnsResolutionFailed or ProxyTestErrorCode.ConnectionTimeout,
            $"Expected DnsResolutionFailed or ConnectionTimeout, got {result.ErrorCode}");
    }

    [Fact]
    public async Task TestAsync_ConnectionRefused_ReturnsConnectionRefused()
    {
        // Pick an unused local port
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop(); // Port is now closed

        var profile = new ProxyProfile
        {
            Id = "p-refused",
            Name = "Refused",
            Host = "127.0.0.1",
            Port = port
        };

        var options = new ProxyTestOptions
        {
            Timeout = TimeSpan.FromSeconds(2),
            TestHttpsConnectTunnel = false
        };

        var result = await _tester.TestAsync(profile, null, options);
        Assert.False(result.IsSuccess);
        Assert.Equal(ProxyTestStage.TcpConnection, result.Stage);
        Assert.Equal(ProxyTestErrorCode.ConnectionRefused, result.ErrorCode);
    }

    [Fact]
    public async Task TestAsync_ProxyReturns403_ReturnsAccessDenied()
    {
        await using var testProxy = new TestProxyServer
        {
            RespondWith403 = true
        };

        var profile = new ProxyProfile
        {
            Id = "p-403",
            Name = "Forbidden",
            Host = testProxy.Host,
            Port = testProxy.Port
        };

        var options = new ProxyTestOptions
        {
            HttpTestUrl = $"http://{testProxy.Host}:{testProxy.Port}/restricted",
            TestHttpsConnectTunnel = false
        };

        var result = await _tester.TestAsync(profile, null, options);
        Assert.False(result.IsSuccess);
        Assert.Equal(ProxyTestStage.DestinationRequest, result.Stage);
        Assert.Equal(ProxyTestErrorCode.AccessDenied, result.ErrorCode);
        Assert.Equal(403, result.HttpStatusCode);
    }

    [Fact]
    public async Task TestAsync_ProxyClosesImmediately_ReturnsUnsupportedBehavior()
    {
        await using var testProxy = new TestProxyServer
        {
            CloseImmediately = true
        };

        var profile = new ProxyProfile
        {
            Id = "p-close",
            Name = "Closer",
            Host = testProxy.Host,
            Port = testProxy.Port
        };

        var options = new ProxyTestOptions
        {
            HttpTestUrl = $"http://{testProxy.Host}:{testProxy.Port}/close",
            TestHttpsConnectTunnel = false
        };

        var result = await _tester.TestAsync(profile, null, options);
        Assert.False(result.IsSuccess);
        Assert.Equal(ProxyTestStage.ProxyHandshake, result.Stage);
        Assert.Equal(ProxyTestErrorCode.UnsupportedProxyBehavior, result.ErrorCode);
    }
}

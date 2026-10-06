using ErgoProxy.Core.Models;
using ErgoProxy.Core.Tunnel;
using ErgoProxy.Tests.Helpers;
using Xunit;

namespace ErgoProxy.Tests.Core;

public class HttpProxyConnectorTests
{
    [Fact]
    public async Task ConnectTunnelAsync_OpenProxy_ReturnsSuccess()
    {
        await using var server = new TestProxyServer { RequireAuth = false };

        var result = await HttpProxyConnector.ConnectTunnelAsync(
            server.Host,
            server.Port,
            "example.com",
            443,
            credentials: null,
            timeout: TimeSpan.FromSeconds(3));

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.Socket);
        Assert.NotNull(result.Stream);

        result.Stream?.Dispose();
    }

    [Fact]
    public async Task ConnectTunnelAsync_AuthRequired_DistinguishesAuthFailure()
    {
        await using var server = new TestProxyServer
        {
            RequireAuth = true,
            ExpectedUser = "student",
            ExpectedPassword = "secret"
        };

        // 1. Wrong credentials
        var badResult = await HttpProxyConnector.ConnectTunnelAsync(
            server.Host,
            server.Port,
            "example.com",
            443,
            credentials: new ProxyCredentials("student", "wrongpass"),
            timeout: TimeSpan.FromSeconds(3));

        Assert.False(badResult.Success);
        Assert.Equal(407, badResult.StatusCode);
        Assert.Equal(TunnelErrorKind.AuthFailed, badResult.ErrorKind);

        // 2. Correct credentials
        var goodResult = await HttpProxyConnector.ConnectTunnelAsync(
            server.Host,
            server.Port,
            "example.com",
            443,
            credentials: new ProxyCredentials("student", "secret"),
            timeout: TimeSpan.FromSeconds(3));

        Assert.True(goodResult.Success);
        Assert.Equal(200, goodResult.StatusCode);
        goodResult.Stream?.Dispose();
    }

    [Fact]
    public async Task ConnectTunnelAsync_Forbidden_DistinguishesPolicyDenied()
    {
        await using var server = new TestProxyServer
        {
            RequireAuth = false,
            RespondWith403 = true
        };

        var result = await HttpProxyConnector.ConnectTunnelAsync(
            server.Host,
            server.Port,
            "blocked.example.com",
            443,
            credentials: null,
            timeout: TimeSpan.FromSeconds(3));

        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(TunnelErrorKind.PolicyDenied, result.ErrorKind);
    }
}

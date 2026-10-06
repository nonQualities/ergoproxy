using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Network;

public interface IProxyTester
{
    Task<ProxyTestResult> TestAsync(
        ProxyProfile profile, 
        ProxyCredentials? credentials = null, 
        ProxyTestOptions? options = null, 
        CancellationToken ct = default);
}

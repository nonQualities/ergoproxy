using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Platform;

public sealed record ProxyApplyResult(
    bool Success, 
    string Message, 
    SystemProxyConfiguration? PreviousSettings = null,
    bool HasConflict = false)
{
    public static ProxyApplyResult Succeeded(string message, SystemProxyConfiguration? previousSettings = null) =>
        new(true, message, previousSettings, false);

    public static ProxyApplyResult Conflict(string message) =>
        new(false, message, null, true);

    public static ProxyApplyResult Failed(string message) =>
        new(false, message, null, false);
}

namespace ErgoProxy.Core.Platform;

public sealed record ProxyRestoreResult(bool Success, string Message)
{
    public static ProxyRestoreResult Succeeded(string message) => new(true, message);
    public static ProxyRestoreResult Failed(string message) => new(false, message);
}

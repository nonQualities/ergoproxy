namespace ErgoProxy.Core.Tunnel.Platform;

public interface ITunnelPlatform
{
    bool IsSupported { get; }
    string? UnsupportedReason { get; }
    string PlatformName { get; }

    Task SetupTunnelNetworkAsync(TunnelAddressing addressing, string? proxyHost, int proxyPort, CancellationToken ct = default);
    Task TearDownTunnelNetworkAsync(TunnelAddressing addressing, CancellationToken ct = default);
    Task CleanStaleStateAsync(TunnelAddressing addressing, CancellationToken ct = default);
}

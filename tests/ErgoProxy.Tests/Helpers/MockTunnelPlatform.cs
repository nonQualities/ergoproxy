using ErgoProxy.Core.Tunnel;
using ErgoProxy.Core.Tunnel.Platform;

namespace ErgoProxy.Tests.Helpers;

public sealed class MockTunnelPlatform : ITunnelPlatform
{
    public bool IsSupported { get; set; } = true;
    public string? UnsupportedReason { get; set; }
    public string PlatformName => "Mock Linux Platform";

    public bool IsNetworkSetup { get; private set; }
    public bool ShouldFailSetup { get; set; }
    public string? SetupFailureMessage { get; set; }

    public int SetupCount { get; private set; }
    public int TearDownCount { get; private set; }
    public int CleanStaleCount { get; private set; }

    public Task SetupTunnelNetworkAsync(TunnelAddressing addressing, string? proxyHost, int proxyPort, CancellationToken ct = default)
    {
        if (ShouldFailSetup)
        {
            throw new InvalidOperationException(SetupFailureMessage ?? "Mock setup failed.");
        }

        IsNetworkSetup = true;
        SetupCount++;
        return Task.CompletedTask;
    }

    public Task TearDownTunnelNetworkAsync(TunnelAddressing addressing, CancellationToken ct = default)
    {
        IsNetworkSetup = false;
        TearDownCount++;
        return Task.CompletedTask;
    }

    public Task CleanStaleStateAsync(TunnelAddressing addressing, CancellationToken ct = default)
    {
        CleanStaleCount++;
        return Task.CompletedTask;
    }
}

using System.Diagnostics;
using ErgoProxy.Core.Models;
using ErgoProxy.Core.Tunnel.Dns;
using ErgoProxy.Core.Tunnel.Platform;

namespace ErgoProxy.Core.Tunnel;

public sealed class TunnelController : IAsyncDisposable
{
    private readonly TunnelAddressing _addressing;
    private readonly ITunnelPlatform _platform;
    private readonly Func<string, IPacketDevice>? _deviceFactory;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private TunnelStatus _status = TunnelStatus.NotRunning();
    private readonly TunnelStats _stats = new();

    private IPacketDevice? _device;
    private NatTable? _natTable;
    private FakeIpPool? _fakeIpPool;
    private FakeDnsServer? _dnsServer;
    private TcpRelay? _tcpRelay;
    private PacketEngine? _packetEngine;

    private CancellationTokenSource? _probeCts;
    private Task? _probeTask;

    public TunnelController(
        TunnelAddressing? addressing = null,
        ITunnelPlatform? platform = null,
        Func<string, IPacketDevice>? deviceFactory = null)
    {
        _addressing = addressing ?? new TunnelAddressing();
        _platform = platform ?? new LinuxTunnelPlatform();
        _deviceFactory = deviceFactory;
    }

    public Task<TunnelStatus> GetStatusAsync(CancellationToken ct = default)
    {
        _status.Stats = _stats;
        return Task.FromResult(_status);
    }

    public async Task<TunnelStatus> StartAsync(TunnelStartRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_status.IsActive)
            {
                return _status;
            }

            // Stage 1: Validating
            _status = new TunnelStatus
            {
                State = TunnelState.Validating,
                Message = "Validating proxy configuration and reachability...",
                ProfileName = request.ProfileName,
                ProxyEndpoint = $"{request.ProxyHost}:{request.ProxyPort}",
                Authentication = !string.IsNullOrEmpty(request.Username),
                InterfaceName = _addressing.InterfaceName,
                DnsMode = "Fake-IP (198.19.0.0/16)"
            };

            if (!_platform.IsSupported)
            {
                _status.State = TunnelState.Error;
                _status.ErrorKind = TunnelErrorKind.PlatformUnsupported;
                _status.Message = _platform.UnsupportedReason ?? "Current platform is not supported.";
                return _status;
            }

            // Pre-flight test to verify proxy connectivity and credentials (Property 4, AC-04, AC-05)
            var preflight = await HttpProxyConnector.ConnectTunnelAsync(
                request.ProxyHost,
                request.ProxyPort,
                request.ProbeHost,
                request.ProbePort,
                request.Credentials,
                fwMark: _addressing.FwMark,
                timeout: TimeSpan.FromSeconds(8),
                ct: ct).ConfigureAwait(false);

            if (!preflight.Success)
            {
                _status.State = TunnelState.Error;
                _status.ErrorKind = preflight.ErrorKind;
                _status.Message = $"Pre-flight proxy connection failed: {preflight.Message}";
                return _status;
            }

            // Stage 2: Connecting (Bringing up TUN device and kernel routing)
            _status.State = TunnelState.Connecting;
            _status.Message = "Creating TUN device and configuring system routes...";

            try
            {
                _device = _deviceFactory != null
                    ? _deviceFactory(_addressing.InterfaceName)
                    : LinuxTunDevice.Open(_addressing.InterfaceName);

                _natTable = new NatTable();
                _fakeIpPool = new FakeIpPool(_addressing);
                _dnsServer = new FakeDnsServer(_fakeIpPool);
                _dnsServer.SetProxyHost(request.ProxyHost);
                _dnsServer.SetBypassDomains(request.BypassRules);

                _tcpRelay = new TcpRelay(_addressing, _natTable, _fakeIpPool, _stats);
                _tcpRelay.ConfigureUpstream(request.ProxyHost, request.ProxyPort, request.Credentials, request.BypassRules);
                _tcpRelay.Start();

                _packetEngine = new PacketEngine(_device, _addressing, _natTable, _dnsServer, _stats, _tcpRelay.ListenPort);
                _packetEngine.Start();

                // Apply OS routes and DNS
                await _platform.SetupTunnelNetworkAsync(_addressing, request.ProxyHost, request.ProxyPort, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await CleanupResourcesAsync().ConfigureAwait(false);
                _status.State = TunnelState.Error;
                _status.ErrorKind = TunnelErrorKind.TunnelFailed;
                _status.Message = $"Failed to establish tunnel: {ex.Message}";
                return _status;
            }

            // Stage 3: Connected!
            _status.State = TunnelState.Connected;
            _status.ErrorKind = TunnelErrorKind.None;
            _status.Message = $"Tunnel active via {request.ProxyHost}:{request.ProxyPort}.";
            _status.ConnectedSince = DateTimeOffset.UtcNow;
            _status.UpstreamReachable = true;
            _status.LastProbeAt = DateTimeOffset.UtcNow;
            _status.LastProbeLatencyMs = preflight.LatencyMs;
            _status.LastProbeMessage = "OK";

            // Start background health probe (AC-16)
            _probeCts = new CancellationTokenSource();
            _probeTask = Task.Run(() => HealthProbeLoopAsync(request, _probeCts.Token));

            return _status;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<TunnelStatus> StopAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_status.IsActive && _status.State == TunnelState.Disconnected)
            {
                return _status;
            }

            _status.State = TunnelState.Disconnecting;
            _status.Message = "Shutting down tunnel and restoring system networking...";

            // Cancel health probe
            if (_probeCts != null)
            {
                _probeCts.Cancel();
                try { if (_probeTask != null) await _probeTask.ConfigureAwait(false); } catch { }
                _probeCts.Dispose();
                _probeCts = null;
                _probeTask = null;
            }

            // Revert OS routes and links (Property 3)
            try
            {
                await _platform.TearDownTunnelNetworkAsync(_addressing, ct).ConfigureAwait(false);
            }
            catch { }

            await CleanupResourcesAsync().ConfigureAwait(false);

            _status = TunnelStatus.NotRunning("Tunnel disconnected. System networking restored.");
            return _status;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task CleanupResourcesAsync()
    {
        _packetEngine?.Dispose();
        _packetEngine = null;

        if (_tcpRelay != null)
        {
            await _tcpRelay.DisposeAsync().ConfigureAwait(false);
            _tcpRelay = null;
        }

        _device?.Dispose();
        _device = null;

        _natTable = null;
        _fakeIpPool = null;
        _dnsServer = null;
    }

    private async Task HealthProbeLoopAsync(TunnelStartRequest request, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);

                var sw = Stopwatch.StartNew();
                var probe = await HttpProxyConnector.ConnectTunnelAsync(
                    request.ProxyHost,
                    request.ProxyPort,
                    request.ProbeHost,
                    request.ProbePort,
                    request.Credentials,
                    fwMark: _addressing.FwMark,
                    timeout: TimeSpan.FromSeconds(5),
                    ct: ct).ConfigureAwait(false);

                sw.Stop();

                _status.LastProbeAt = DateTimeOffset.UtcNow;
                _status.LastProbeLatencyMs = sw.Elapsed.TotalMilliseconds;

                if (probe.Success)
                {
                    _status.UpstreamReachable = true;
                    _status.LastProbeMessage = "OK";
                    if (_status.State == TunnelState.Degraded)
                    {
                        _status.State = TunnelState.Connected;
                        _status.Message = $"Upstream proxy reconnected ({request.ProxyHost}:{request.ProxyPort}).";
                    }
                }
                else
                {
                    _status.UpstreamReachable = false;
                    _status.LastProbeMessage = probe.Message;
                    if (_status.State == TunnelState.Connected)
                    {
                        _status.State = TunnelState.Degraded;
                        _status.Message = $"Upstream proxy unreachable ({probe.ErrorKind}): {probe.Message}";
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _status.UpstreamReachable = false;
                _status.LastProbeMessage = ex.Message;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lock.Dispose();
    }
}

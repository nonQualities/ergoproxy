using System.Net;
using System.Net.Sockets;
using ErgoProxy.Core.Models;
using ErgoProxy.Core.Tunnel.Dns;
using ErgoProxy.Core.Tunnel.Native;
using ErgoProxy.Core.Tunnel.Packets;

namespace ErgoProxy.Core.Tunnel;

/// <summary>
/// TCP relay listening on 198.18.0.1:listenPort. Accepts re-injected TCP connections from the TUN,
/// correlates their source NAT port back to the original destination, establishes an HTTP CONNECT
/// tunnel through the upstream HTTP proxy, and splices data bidirectionally.
/// </summary>
public sealed class TcpRelay : IAsyncDisposable
{
    private readonly TunnelAddressing _addressing;
    private readonly NatTable _natTable;
    private readonly FakeIpPool _fakeIpPool;
    private readonly TunnelStats _stats;
    private readonly Socket _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptTask;

    private string _proxyHost = string.Empty;
    private int _proxyPort = 8080;
    private ProxyCredentials? _credentials;
    private readonly HashSet<string> _bypassHosts = new(StringComparer.OrdinalIgnoreCase);

    public int ListenPort { get; }

    public TcpRelay(
        TunnelAddressing addressing,
        NatTable natTable,
        FakeIpPool fakeIpPool,
        TunnelStats stats,
        int listenPort = 10800)
    {
        _addressing = addressing;
        _natTable = natTable;
        _fakeIpPool = fakeIpPool;
        _stats = stats;

        _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        try
        {
            _listener.Bind(new IPEndPoint(_addressing.InterfaceAddress, listenPort));
        }
        catch (SocketException)
        {
            _listener.Bind(new IPEndPoint(IPAddress.Any, listenPort));
        }
        _listener.Listen(1024);
        ListenPort = ((IPEndPoint)_listener.LocalEndPoint!).Port;
    }

    public void ConfigureUpstream(string proxyHost, int proxyPort, ProxyCredentials? credentials, IEnumerable<string> bypassRules)
    {
        _proxyHost = proxyHost;
        _proxyPort = proxyPort;
        _credentials = credentials;
        _bypassHosts.Clear();
        foreach (var rule in bypassRules)
        {
            var clean = rule.Trim();
            if (!string.IsNullOrEmpty(clean))
            {
                _bypassHosts.Add(clean);
            }
        }
    }

    public void Start()
    {
        _acceptTask = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var clientSocket = await _listener.AcceptAsync(_cts.Token).ConfigureAwait(false);
                _ = HandleClientAsync(clientSocket, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
            catch (Exception)
            {
                // Accept error, brief delay to prevent spin
                await Task.Delay(50, _cts.Token).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(Socket clientSocket, CancellationToken ct)
    {
        Interlocked.Increment(ref _stats.TotalConnections);
        Interlocked.Increment(ref _stats.ActiveConnections);

        ushort natPort = 0;
        try
        {
            var remoteEp = (IPEndPoint)clientSocket.RemoteEndPoint!;
            natPort = (ushort)remoteEp.Port;

            var flowKey = _natTable.GetByNatPort(natPort);
            if (!flowKey.HasValue)
            {
                // Unknown flow
                Interlocked.Increment(ref _stats.FailedConnections);
                clientSocket.Dispose();
                return;
            }

            _natTable.Acquire(natPort);

            // Determine target host and port
            var destIp = flowKey.Value.DestinationIp;
            var destPort = flowKey.Value.DestinationPort;
            var targetHost = _fakeIpPool.ResolveHost(destIp) ?? IPv4.Format(destIp);

            // Check if bypass
            var isBypass = IsBypass(targetHost, destIp);
            Socket? upstreamSocket = null;
            Stream? upstreamStream = null;

            if (isBypass)
            {
                Interlocked.Increment(ref _stats.DirectConnections);
                upstreamSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                LinuxNative.SetMark(upstreamSocket, _addressing.FwMark);
                await upstreamSocket.ConnectAsync(targetHost, destPort, ct).ConfigureAwait(false);
                upstreamStream = new NetworkStream(upstreamSocket, ownsSocket: true);
            }
            else
            {
                var connectResult = await HttpProxyConnector.ConnectTunnelAsync(
                    _proxyHost,
                    _proxyPort,
                    targetHost,
                    destPort,
                    _credentials,
                    fwMark: _addressing.FwMark,
                    timeout: TimeSpan.FromSeconds(15),
                    ct: ct).ConfigureAwait(false);

                if (!connectResult.Success)
                {
                    Interlocked.Increment(ref _stats.FailedConnections);
                    if (connectResult.ErrorKind == TunnelErrorKind.AuthFailed)
                        Interlocked.Increment(ref _stats.ProxyAuthFailures);
                    else if (connectResult.ErrorKind == TunnelErrorKind.PolicyDenied)
                        Interlocked.Increment(ref _stats.ProxyPolicyDenials);
                    else if (connectResult.ErrorKind == TunnelErrorKind.DestinationFailed)
                        Interlocked.Increment(ref _stats.DestinationFailures);

                    clientSocket.Dispose();
                    return;
                }

                upstreamSocket = connectResult.Socket!;
                upstreamStream = connectResult.Stream!;
            }

            using (clientSocket)
            using (upstreamSocket)
            using (upstreamStream)
            using (var clientStream = new NetworkStream(clientSocket, ownsSocket: false))
            {
                var clientToUpstream = CopyStreamAsync(clientStream, upstreamStream, isUpstream: true, ct);
                var upstreamToClient = CopyStreamAsync(upstreamStream, clientStream, isUpstream: false, ct);

                await Task.WhenAny(clientToUpstream, upstreamToClient).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            Interlocked.Increment(ref _stats.FailedConnections);
        }
        finally
        {
            Interlocked.Decrement(ref _stats.ActiveConnections);
            if (natPort != 0)
            {
                _natTable.Release(natPort);
            }
        }
    }

    private async Task CopyStreamAsync(Stream source, Stream destination, bool isUpstream, CancellationToken ct)
    {
        var buffer = new byte[16384];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var bytesRead = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (bytesRead == 0) break;

                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);

                if (isUpstream)
                {
                    Interlocked.Add(ref _stats.BytesUp, bytesRead);
                }
                else
                {
                    Interlocked.Add(ref _stats.BytesDown, bytesRead);
                }
            }
        }
        catch
        {
            // Socket closed or aborted
        }
    }

    private bool IsBypass(string targetHost, uint destIp)
    {
        if (_bypassHosts.Contains(targetHost)) return true;

        var targetIpStr = IPv4.Format(destIp);
        if (_bypassHosts.Contains(targetIpStr)) return true;

        foreach (var rule in _bypassHosts)
        {
            if (rule.StartsWith("*.") && targetHost.EndsWith(rule[1..], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Dispose();
        if (_acceptTask != null)
        {
            try { await _acceptTask.ConfigureAwait(false); } catch { }
        }
        _cts.Dispose();
    }
}

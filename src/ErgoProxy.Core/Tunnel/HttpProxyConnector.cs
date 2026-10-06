using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ErgoProxy.Core.Models;
using ErgoProxy.Core.Security;
using ErgoProxy.Core.Tunnel.Native;

namespace ErgoProxy.Core.Tunnel;

public sealed record ProxyConnectResult(
    bool Success,
    Socket? Socket,
    NetworkStream? Stream,
    int StatusCode,
    TunnelErrorKind ErrorKind,
    string Message,
    double LatencyMs = 0);

/// <summary>
/// Handles TCP connection and HTTP CONNECT or GET negotiation with the upstream HTTP proxy.
/// Applies socket firewall marks (SO_MARK) on Linux to ensure proxy traffic bypasses the tunnel (Property 2).
/// </summary>
public static class HttpProxyConnector
{
    public static async Task<ProxyConnectResult> ConnectTunnelAsync(
        string proxyHost,
        int proxyPort,
        string targetHost,
        int targetPort,
        ProxyCredentials? credentials,
        uint fwMark = 0,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(10);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(effectiveTimeout);
        var token = timeoutCts.Token;

        Socket? socket = null;
        try
        {
            socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            if (fwMark != 0)
            {
                LinuxNative.SetMark(socket, fwMark);
            }

            // Resolve proxy address
            IPAddress[] addrs;
            try
            {
                addrs = await System.Net.Dns.GetHostAddressesAsync(proxyHost, token).ConfigureAwait(false);
                if (addrs.Length == 0)
                {
                    return new ProxyConnectResult(false, null, null, 0, TunnelErrorKind.ProxyDnsFailed,
                        $"DNS resolution returned no IP addresses for proxy host '{proxyHost}'.", sw.Elapsed.TotalMilliseconds);
                }
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                return new ProxyConnectResult(false, null, null, 0,
                    token.IsCancellationRequested && !ct.IsCancellationRequested ? TunnelErrorKind.ProxyTimeout : TunnelErrorKind.ProxyDnsFailed,
                    $"Failed to resolve proxy host '{proxyHost}': {ex.Message}", sw.Elapsed.TotalMilliseconds);
            }

            var endpoint = new IPEndPoint(addrs[0], proxyPort);
            await socket.ConnectAsync(endpoint, token).ConfigureAwait(false);

            // Construct HTTP CONNECT request
            var targetAuthority = $"{targetHost}:{targetPort}";
            var connectReq = new StringBuilder();
            connectReq.Append($"CONNECT {targetAuthority} HTTP/1.1\r\n");
            connectReq.Append($"Host: {targetAuthority}\r\n");
            connectReq.Append("User-Agent: ErgoProxy/1.0\r\n");
            connectReq.Append("Proxy-Connection: keep-alive\r\n");

            if (credentials != null && !string.IsNullOrWhiteSpace(credentials.Username))
            {
                var authBytes = Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}");
                var authHeader = Convert.ToBase64String(authBytes);
                connectReq.Append($"Proxy-Authorization: Basic {authHeader}\r\n");
            }
            connectReq.Append("\r\n");

            var reqBytes = Encoding.UTF8.GetBytes(connectReq.ToString());
            await socket.SendAsync(reqBytes, SocketFlags.None, token).ConfigureAwait(false);

            var stream = new NetworkStream(socket, ownsSocket: true);
            var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

            var statusLine = await reader.ReadLineAsync(token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(statusLine))
            {
                stream.Dispose();
                return new ProxyConnectResult(false, null, null, 0, TunnelErrorKind.MalformedProxyResponse,
                    "Proxy closed connection without answering HTTP CONNECT command.", sw.Elapsed.TotalMilliseconds);
            }

            var parts = statusLine.Split(' ', 3);
            if (parts.Length >= 2 && int.TryParse(parts[1], out var statusCode))
            {
                if (statusCode == 200)
                {
                    // Read headers until blank line
                    string? headerLine;
                    while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync(token).ConfigureAwait(false)))
                    {
                    }

                    return new ProxyConnectResult(true, socket, stream, 200, TunnelErrorKind.None,
                        $"CONNECT tunnel established to {targetAuthority}.", sw.Elapsed.TotalMilliseconds);
                }

                stream.Dispose();
                if (statusCode == 407)
                {
                    return new ProxyConnectResult(false, null, null, 407, TunnelErrorKind.AuthFailed,
                        "Proxy authentication required (HTTP 407). Check your proxy username and password.", sw.Elapsed.TotalMilliseconds);
                }

                if (statusCode == 403)
                {
                    return new ProxyConnectResult(false, null, null, 403, TunnelErrorKind.PolicyDenied,
                        $"Proxy refused CONNECT tunnel to {targetAuthority} (HTTP 403 Forbidden).", sw.Elapsed.TotalMilliseconds);
                }

                if (statusCode is 502 or 503 or 504)
                {
                    return new ProxyConnectResult(false, null, null, statusCode, TunnelErrorKind.DestinationFailed,
                        $"Upstream proxy failed to connect to destination {targetAuthority} (HTTP {statusCode}).", sw.Elapsed.TotalMilliseconds);
                }

                return new ProxyConnectResult(false, null, null, statusCode, TunnelErrorKind.MalformedProxyResponse,
                    $"Proxy rejected CONNECT request: {SecretRedactor.Redact(statusLine)}", sw.Elapsed.TotalMilliseconds);
            }

            stream.Dispose();
            return new ProxyConnectResult(false, null, null, 0, TunnelErrorKind.MalformedProxyResponse,
                $"Invalid status line from proxy: {SecretRedactor.Redact(statusLine)}", sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            socket?.Dispose();
            return new ProxyConnectResult(false, null, null, 0, TunnelErrorKind.ProxyTimeout,
                $"Proxy connection timed out after {effectiveTimeout.TotalSeconds:F1}s.", sw.Elapsed.TotalMilliseconds);
        }
        catch (SocketException ex)
        {
            socket?.Dispose();
            var kind = ex.SocketErrorCode == SocketError.ConnectionRefused
                ? TunnelErrorKind.ProxyUnreachable
                : TunnelErrorKind.ProxyUnreachable;
            return new ProxyConnectResult(false, null, null, 0, kind,
                $"Failed to connect to proxy {proxyHost}:{proxyPort}: {ex.Message}", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            socket?.Dispose();
            return new ProxyConnectResult(false, null, null, 0, TunnelErrorKind.Unknown,
                $"Error establishing tunnel via proxy: {ex.Message}", sw.Elapsed.TotalMilliseconds);
        }
    }
}

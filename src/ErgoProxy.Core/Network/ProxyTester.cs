using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using ErgoProxy.Core.Models;
using ErgoProxy.Core.Security;
using ErgoProxy.Core.Validation;

namespace ErgoProxy.Core.Network;

public sealed class ProxyTester : IProxyTester
{
    private readonly IProfileValidator _validator;

    public ProxyTester(IProfileValidator? validator = null)
    {
        _validator = validator ?? new ProfileValidator();
    }

    public async Task<ProxyTestResult> TestAsync(
        ProxyProfile profile, 
        ProxyCredentials? credentials = null, 
        ProxyTestOptions? options = null, 
        CancellationToken ct = default)
    {
        options ??= ProxyTestOptions.Default;
        var sw = Stopwatch.StartNew();

        // Stage 1: Configuration Validation
        var validation = _validator.Validate(profile);
        if (!validation.IsValid)
        {
            return ProxyTestResult.Failure(
                ProxyTestStage.Configuration,
                ProxyTestErrorCode.InvalidConfig,
                $"Invalid profile configuration: {string.Join("; ", validation.Errors)}",
                latencyMs: 0);
        }

        if (profile.AuthenticationEnabled && credentials == null)
        {
            return ProxyTestResult.Failure(
                ProxyTestStage.Configuration,
                ProxyTestErrorCode.InvalidConfig,
                "Authentication is enabled for this profile, but no credentials were provided.",
                latencyMs: 0);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(options.Timeout);
        var token = timeoutCts.Token;

        IPAddress[] ipAddresses;

        // Stage 2: DNS Resolution
        try
        {
            ipAddresses = await Dns.GetHostAddressesAsync(profile.Host, token).ConfigureAwait(false);
            if (ipAddresses.Length == 0)
            {
                return ProxyTestResult.Failure(
                    ProxyTestStage.DnsResolution,
                    ProxyTestErrorCode.DnsResolutionFailed,
                    $"DNS resolution returned no IP addresses for host '{profile.Host}'.",
                    sw.Elapsed.TotalMilliseconds);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ProxyTestResult.Failure(
                ProxyTestStage.DnsResolution,
                ProxyTestErrorCode.ConnectionTimeout,
                $"DNS resolution timed out for proxy host '{profile.Host}'.",
                sw.Elapsed.TotalMilliseconds);
        }
        catch (SocketException ex)
        {
            return ProxyTestResult.Failure(
                ProxyTestStage.DnsResolution,
                ProxyTestErrorCode.DnsResolutionFailed,
                $"DNS resolution failed for proxy host '{profile.Host}': {ex.Message}",
                sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            return ProxyTestResult.Failure(
                ProxyTestStage.DnsResolution,
                ProxyTestErrorCode.DnsResolutionFailed,
                $"DNS lookup error for '{profile.Host}': {ex.Message}",
                sw.Elapsed.TotalMilliseconds);
        }

        var targetEndpoint = new IPEndPoint(ipAddresses[0], profile.Port);

        // Stage 3 & 4: Direct HTTP Request test
        var httpDirectResult = await TestDirectHttpAsync(profile, targetEndpoint, credentials, options, sw, token)
            .ConfigureAwait(false);

        if (!httpDirectResult.IsSuccess)
        {
            return httpDirectResult;
        }

        // Stage 5: HTTPS CONNECT Tunnel Test (if enabled)
        if (options.TestHttpsConnectTunnel)
        {
            var connectResult = await TestHttpsConnectTunnelAsync(profile, targetEndpoint, credentials, options, sw, token)
                .ConfigureAwait(false);

            if (!connectResult.IsSuccess)
            {
                return connectResult;
            }

            sw.Stop();
            return ProxyTestResult.Success(
                $"Proxy connectivity verified: HTTP request and HTTPS CONNECT tunnel succeeded ({profile.Host}:{profile.Port}).",
                sw.Elapsed.TotalMilliseconds,
                options.HttpsTestHost,
                connectTunnelSuccess: true,
                statusCode: 200);
        }

        sw.Stop();
        return ProxyTestResult.Success(
            $"Proxy connectivity verified: HTTP request succeeded ({profile.Host}:{profile.Port}).",
            sw.Elapsed.TotalMilliseconds,
            options.HttpTestUrl,
            connectTunnelSuccess: false,
            statusCode: httpDirectResult.HttpStatusCode ?? 200);
    }

    private static async Task<ProxyTestResult> TestDirectHttpAsync(
        ProxyProfile profile,
        IPEndPoint endpoint,
        ProxyCredentials? credentials,
        ProxyTestOptions options,
        Stopwatch sw,
        CancellationToken ct)
    {
        Socket? socket = null;
        try
        {
            socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(endpoint, ct).ConfigureAwait(false);

            // Construct HTTP request
            var targetUri = new Uri(options.HttpTestUrl);
            var reqBuilder = new StringBuilder();
            reqBuilder.Append($"GET {options.HttpTestUrl} HTTP/1.1\r\n");
            reqBuilder.Append($"Host: {targetUri.Host}\r\n");
            reqBuilder.Append("User-Agent: ErgoProxy/1.0\r\n");
            reqBuilder.Append("Connection: close\r\n");

            if (profile.AuthenticationEnabled && credentials != null)
            {
                var authBytes = Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}");
                var authHeader = Convert.ToBase64String(authBytes);
                reqBuilder.Append($"Proxy-Authorization: Basic {authHeader}\r\n");
            }
            reqBuilder.Append("\r\n");

            var reqBytes = Encoding.UTF8.GetBytes(reqBuilder.ToString());
            await socket.SendAsync(reqBytes, SocketFlags.None, ct).ConfigureAwait(false);

            // Read response status
            var buffer = new byte[4096];
            var bytesRead = await socket.ReceiveAsync(buffer, SocketFlags.None, ct).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return ProxyTestResult.Failure(
                    ProxyTestStage.ProxyHandshake,
                    ProxyTestErrorCode.UnsupportedProxyBehavior,
                    "Proxy closed connection without returning HTTP response headers.",
                    sw.Elapsed.TotalMilliseconds);
            }

            var responseText = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            var firstLine = responseText.Split('\n')[0].Trim();
            var parts = firstLine.Split(' ', 3);

            if (parts.Length >= 2 && int.TryParse(parts[1], out var statusCode))
            {
                if (statusCode == 407)
                {
                    return ProxyTestResult.Failure(
                        ProxyTestStage.ProxyAuth,
                        ProxyTestErrorCode.AuthFailed,
                        "Proxy authentication failed (HTTP 407 Proxy Authentication Required).",
                        sw.Elapsed.TotalMilliseconds,
                        statusCode: 407,
                        targetUrl: options.HttpTestUrl);
                }

                if (statusCode == 403)
                {
                    return ProxyTestResult.Failure(
                        ProxyTestStage.DestinationRequest,
                        ProxyTestErrorCode.AccessDenied,
                        "Proxy refused destination access (HTTP 403 Forbidden).",
                        sw.Elapsed.TotalMilliseconds,
                        statusCode: 403,
                        targetUrl: options.HttpTestUrl);
                }

                if (statusCode is >= 200 and < 400)
                {
                    return ProxyTestResult.Success(
                        $"HTTP request succeeded ({firstLine}).",
                        sw.Elapsed.TotalMilliseconds,
                        options.HttpTestUrl,
                        connectTunnelSuccess: false,
                        statusCode: statusCode);
                }

                return ProxyTestResult.Failure(
                    ProxyTestStage.DestinationRequest,
                    ProxyTestErrorCode.HttpError,
                    $"Proxy returned HTTP status {statusCode} ({firstLine}).",
                    sw.Elapsed.TotalMilliseconds,
                    statusCode: statusCode,
                    targetUrl: options.HttpTestUrl);
            }

            return ProxyTestResult.Failure(
                ProxyTestStage.ProxyHandshake,
                ProxyTestErrorCode.UnsupportedProxyBehavior,
                $"Malformed response from proxy: {SecretRedactor.Redact(firstLine)}",
                sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            return ProxyTestResult.Failure(
                ProxyTestStage.TcpConnection,
                ProxyTestErrorCode.ConnectionTimeout,
                $"Connection to proxy '{profile.Host}:{profile.Port}' timed out after {options.Timeout.TotalSeconds:F1}s.",
                sw.Elapsed.TotalMilliseconds);
        }
        catch (SocketException ex)
        {
            var errCode = ex.SocketErrorCode == SocketError.ConnectionRefused
                ? ProxyTestErrorCode.ConnectionRefused
                : ProxyTestErrorCode.UnknownError;

            return ProxyTestResult.Failure(
                ProxyTestStage.TcpConnection,
                errCode,
                $"TCP connection failed to '{profile.Host}:{profile.Port}': {ex.Message}",
                sw.Elapsed.TotalMilliseconds);
        }
        finally
        {
            socket?.Dispose();
        }
    }

    private static async Task<ProxyTestResult> TestHttpsConnectTunnelAsync(
        ProxyProfile profile,
        IPEndPoint endpoint,
        ProxyCredentials? credentials,
        ProxyTestOptions options,
        Stopwatch sw,
        CancellationToken ct)
    {
        Socket? socket = null;
        try
        {
            socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(endpoint, ct).ConfigureAwait(false);

            // Send CONNECT command
            var targetAuthority = $"{options.HttpsTestHost}:{options.HttpsTestPort}";
            var connectReq = new StringBuilder();
            connectReq.Append($"CONNECT {targetAuthority} HTTP/1.1\r\n");
            connectReq.Append($"Host: {targetAuthority}\r\n");
            connectReq.Append("User-Agent: ErgoProxy/1.0\r\n");

            if (profile.AuthenticationEnabled && credentials != null)
            {
                var authBytes = Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}");
                var authHeader = Convert.ToBase64String(authBytes);
                connectReq.Append($"Proxy-Authorization: Basic {authHeader}\r\n");
            }
            connectReq.Append("\r\n");

            var connectBytes = Encoding.UTF8.GetBytes(connectReq.ToString());
            await socket.SendAsync(connectBytes, SocketFlags.None, ct).ConfigureAwait(false);

            // Read CONNECT response header line-by-line
            var stream = new NetworkStream(socket, ownsSocket: true);
            var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

            var statusLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(statusLine))
            {
                return ProxyTestResult.Failure(
                    ProxyTestStage.ConnectTunnel,
                    ProxyTestErrorCode.UnsupportedProxyBehavior,
                    "Proxy closed connection without answering HTTP CONNECT command.",
                    sw.Elapsed.TotalMilliseconds);
            }

            var parts = statusLine.Split(' ', 3);
            if (parts.Length >= 2 && int.TryParse(parts[1], out var connectStatus))
            {
                if (connectStatus == 407)
                {
                    return ProxyTestResult.Failure(
                        ProxyTestStage.ProxyAuth,
                        ProxyTestErrorCode.AuthFailed,
                        "Proxy authentication failed during HTTP CONNECT (HTTP 407).",
                        sw.Elapsed.TotalMilliseconds,
                        statusCode: 407);
                }

                if (connectStatus == 403)
                {
                    return ProxyTestResult.Failure(
                        ProxyTestStage.DestinationRequest,
                        ProxyTestErrorCode.AccessDenied,
                        $"Proxy denied CONNECT tunnel to {targetAuthority} (HTTP 403).",
                        sw.Elapsed.TotalMilliseconds,
                        statusCode: 403);
                }

                if (connectStatus != 200)
                {
                    return ProxyTestResult.Failure(
                        ProxyTestStage.ConnectTunnel,
                        ProxyTestErrorCode.UnsupportedProxyBehavior,
                        $"Proxy CONNECT failed with status {connectStatus}: {statusLine}",
                        sw.Elapsed.TotalMilliseconds,
                        statusCode: connectStatus);
                }
            }
            else
            {
                return ProxyTestResult.Failure(
                    ProxyTestStage.ConnectTunnel,
                    ProxyTestErrorCode.UnsupportedProxyBehavior,
                    $"Unexpected CONNECT response: {SecretRedactor.Redact(statusLine)}",
                    sw.Elapsed.TotalMilliseconds);
            }

            // Consume remaining headers until empty line
            string? headerLine;
            while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync(ct).ConfigureAwait(false)))
            {
                // drain headers
            }

            // Established tunnel! Now test TLS handshake
            try
            {
                var sslStream = new SslStream(
                    stream, 
                    leaveInnerStreamOpen: false, 
                    userCertificateValidationCallback: (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        if (!options.ValidateTlsCertificate) return true;
                        return sslPolicyErrors == SslPolicyErrors.None;
                    });

                await sslStream.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions
                    {
                        TargetHost = options.HttpsTestHost
                    },
                    ct).ConfigureAwait(false);

                // Send simple encrypted HTTP GET request through the tunnel
                var httpsReq = $"GET / HTTP/1.1\r\nHost: {options.HttpsTestHost}\r\nUser-Agent: ErgoProxy/1.0\r\nConnection: close\r\n\r\n";
                var httpsBytes = Encoding.UTF8.GetBytes(httpsReq);
                await sslStream.WriteAsync(httpsBytes, ct).ConfigureAwait(false);

                var sslBuffer = new byte[1024];
                var readCount = await sslStream.ReadAsync(sslBuffer, ct).ConfigureAwait(false);
                if (readCount > 0)
                {
                    return ProxyTestResult.Success(
                        $"CONNECT tunnel and TLS handshake verified for {targetAuthority}.",
                        sw.Elapsed.TotalMilliseconds,
                        options.HttpsTestHost,
                        connectTunnelSuccess: true,
                        statusCode: 200);
                }

                return ProxyTestResult.Failure(
                    ProxyTestStage.DestinationRequest,
                    ProxyTestErrorCode.HttpError,
                    "TLS handshake succeeded but destination sent 0 bytes.",
                    sw.Elapsed.TotalMilliseconds);
            }
            catch (AuthenticationException ex)
            {
                return ProxyTestResult.Failure(
                    ProxyTestStage.TlsHandshake,
                    ProxyTestErrorCode.TlsValidationFailed,
                    $"TLS certificate validation failed inside tunnel to {options.HttpsTestHost}: {ex.Message}",
                    sw.Elapsed.TotalMilliseconds);
            }
        }
        catch (OperationCanceledException)
        {
            return ProxyTestResult.Failure(
                ProxyTestStage.ConnectTunnel,
                ProxyTestErrorCode.ConnectionTimeout,
                $"HTTP CONNECT test to '{options.HttpsTestHost}' timed out.",
                sw.Elapsed.TotalMilliseconds);
        }
        catch (SocketException ex)
        {
            return ProxyTestResult.Failure(
                ProxyTestStage.ConnectTunnel,
                ProxyTestErrorCode.ConnectionRefused,
                $"Socket error during CONNECT test: {ex.Message}",
                sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            return ProxyTestResult.Failure(
                ProxyTestStage.ConnectTunnel,
                ProxyTestErrorCode.UnknownError,
                $"Error during CONNECT tunnel test: {ex.Message}",
                sw.Elapsed.TotalMilliseconds);
        }
    }
}

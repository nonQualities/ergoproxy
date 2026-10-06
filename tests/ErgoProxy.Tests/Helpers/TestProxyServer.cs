using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ErgoProxy.Tests.Helpers;

public sealed class TestProxyServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;
    private readonly X509Certificate2 _testCert;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public string Host => "127.0.0.1";

    public bool RequireAuth { get; set; }
    public string ExpectedUser { get; set; } = "testuser";
    public string ExpectedPassword { get; set; } = "secret123";
    public bool RespondWith403 { get; set; }
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;
    public bool CloseImmediately { get; set; }

    public TestProxyServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();

        // Generate temporary test self-signed certificate for TLS testing
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        _testCert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));

        _listenTask = Task.Run(AcceptConnectionsLoopAsync);
    }

    private async Task AcceptConnectionsLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, _cts.Token));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true))
        {
            if (CloseImmediately)
            {
                return;
            }

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, ct).ConfigureAwait(false);
            }

            var requestLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(requestLine)) return;

            string? authHeader = null;
            string? headerLine;
            while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync(ct).ConfigureAwait(false)))
            {
                if (headerLine.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase))
                {
                    authHeader = headerLine["Proxy-Authorization:".Length..].Trim();
                }
            }

            // Auth verification
            if (RequireAuth)
            {
                var expectedBasic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ExpectedUser}:{ExpectedPassword}"));
                if (authHeader == null || !string.Equals(authHeader, expectedBasic, StringComparison.Ordinal))
                {
                    var authChallenge = "HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm=\"Test\"\r\nContent-Length: 0\r\n\r\n";
                    var challengeBytes = Encoding.ASCII.GetBytes(authChallenge);
                    await stream.WriteAsync(challengeBytes, ct).ConfigureAwait(false);
                    return;
                }
            }

            if (RespondWith403)
            {
                var forbidden = "HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(forbidden), ct).ConfigureAwait(false);
                return;
            }

            if (requestLine.StartsWith("CONNECT ", StringComparison.OrdinalIgnoreCase))
            {
                // Send 200 Connection Established
                var okConnect = "HTTP/1.1 200 Connection Established\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(okConnect), ct).ConfigureAwait(false);

                // Now act as the destination server and perform TLS handshake
                try
                {
                    var sslStream = new SslStream(stream, leaveInnerStreamOpen: true);
                    await sslStream.AuthenticateAsServerAsync(_testCert, clientCertificateRequired: false, checkCertificateRevocation: false).ConfigureAwait(false);

                    var sslBuffer = new byte[1024];
                    var read = await sslStream.ReadAsync(sslBuffer, ct).ConfigureAwait(false);
                    if (read > 0)
                    {
                        var response = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nHELLO";
                        await sslStream.WriteAsync(Encoding.ASCII.GetBytes(response), ct).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // client TLS might complete or fail
                }
            }
            else
            {
                // Ordinary HTTP GET
                var okResponse = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 2\r\n\r\nOK";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(okResponse), ct).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        if (_listenTask != null)
        {
            try { await _listenTask.ConfigureAwait(false); } catch { }
        }
        _testCert.Dispose();
        _cts.Dispose();
    }
}

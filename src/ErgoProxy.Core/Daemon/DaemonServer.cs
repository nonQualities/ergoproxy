using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ErgoProxy.Core.Models;
using ErgoProxy.Core.Tunnel;
using ErgoProxy.Core.Tunnel.Native;

namespace ErgoProxy.Core.Daemon;

public sealed class DaemonServer : IAsyncDisposable
{
    private readonly TunnelController _controller;
    private readonly string _endpoint;
    private readonly uint? _targetUid;
    private Socket? _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptTask;

    public DaemonServer(TunnelController controller, string? endpoint = null, uint? targetUid = null)
    {
        _controller = controller;
        _endpoint = endpoint ?? DaemonClient.DefaultEndpoint;
        _targetUid = targetUid ?? GetSudoUid();
    }

    public async Task StartAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            _acceptTask = Task.Run(NamedPipeAcceptLoopAsync);
        }
        else
        {
            var dir = Path.GetDirectoryName(_endpoint);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(_endpoint))
            {
                try { File.Delete(_endpoint); } catch { }
            }

            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(_endpoint));
            _listener.Listen(16);

            // Adjust ownership / permissions so the invoking user can access the socket
            if (OperatingSystem.IsLinux() && _targetUid.HasValue)
            {
                LinuxNative.Chown(_endpoint, _targetUid.Value, _targetUid.Value);
            }

            _acceptTask = Task.Run(UnixSocketAcceptLoopAsync);
        }

        // Register process exit hook for clean reversibility
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Console.CancelKeyPress += OnCancelKeyPress;
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        try { _controller.StopAsync().GetAwaiter().GetResult(); } catch { }
        CleanupSocketFile();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _cts.Cancel();
        try { _controller.StopAsync().GetAwaiter().GetResult(); } catch { }
        CleanupSocketFile();
    }

    private async Task UnixSocketAcceptLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptAsync(_cts.Token).ConfigureAwait(false);
                var stream = new NetworkStream(client, ownsSocket: true);
                _ = HandleClientStreamAsync(stream, _cts.Token);
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
                await Task.Delay(50, _cts.Token).ConfigureAwait(false);
            }
        }
    }

    private async Task NamedPipeAcceptLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var serverStream = new NamedPipeServerStream(
                    _endpoint,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await serverStream.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                _ = HandleClientStreamAsync(serverStream, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                await Task.Delay(50, _cts.Token).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientStreamAsync(Stream stream, CancellationToken ct)
    {
        using (stream)
        using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
        using (var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            try
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line)) return;

                var request = JsonSerializer.Deserialize<ControlRequest>(line);
                if (request == null) return;

                ControlResponse response;

                switch (request.Command.ToLowerInvariant())
                {
                    case "status":
                        var status = await _controller.GetStatusAsync(ct).ConfigureAwait(false);
                        response = ControlResponse.Ok("Status retrieved", status);
                        break;

                    case "start":
                        if (request.StartRequest == null)
                        {
                            response = ControlResponse.Fail("Missing StartRequest payload.");
                        }
                        else
                        {
                            var startStatus = await _controller.StartAsync(request.StartRequest, ct).ConfigureAwait(false);
                            response = startStatus.State == TunnelState.Connected
                                ? ControlResponse.Ok(startStatus.Message, startStatus)
                                : ControlResponse.Fail(startStatus.Message, startStatus);
                        }
                        break;

                    case "stop":
                        var stopStatus = await _controller.StopAsync(ct).ConfigureAwait(false);
                        response = ControlResponse.Ok(stopStatus.Message, stopStatus);
                        break;

                    case "shutdown":
                        await _controller.StopAsync(ct).ConfigureAwait(false);
                        response = ControlResponse.Ok("Daemon shutting down.");
                        _cts.Cancel();
                        break;

                    default:
                        response = ControlResponse.Fail($"Unknown command '{request.Command}'.");
                        break;
                }

                var responseJson = JsonSerializer.Serialize(response);
                await writer.WriteLineAsync(responseJson.AsMemory(), ct).ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try
                {
                    var errJson = JsonSerializer.Serialize(ControlResponse.Fail($"Internal error: {ex.Message}"));
                    await writer.WriteLineAsync(errJson.AsMemory(), ct).ConfigureAwait(false);
                    await writer.FlushAsync(ct).ConfigureAwait(false);
                }
                catch { }
            }
        }
    }

    public async Task WaitForShutdownAsync()
    {
        try
        {
            await Task.Delay(Timeout.Infinite, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    private static uint? GetSudoUid()
    {
        var sudoUidStr = Environment.GetEnvironmentVariable("SUDO_UID");
        if (!string.IsNullOrEmpty(sudoUidStr) && uint.TryParse(sudoUidStr, out var uid))
        {
            return uid;
        }
        return null;
    }

    private void CleanupSocketFile()
    {
        if (!OperatingSystem.IsWindows() && File.Exists(_endpoint))
        {
            try { File.Delete(_endpoint); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener?.Dispose();
        if (_acceptTask != null)
        {
            try { await _acceptTask.ConfigureAwait(false); } catch { }
        }
        await _controller.DisposeAsync().ConfigureAwait(false);
        CleanupSocketFile();
        _cts.Dispose();
    }
}

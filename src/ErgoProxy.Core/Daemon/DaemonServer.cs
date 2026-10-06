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
    private readonly string _socketPath;
    private readonly uint? _targetUid;
    private Socket? _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptTask;

    public DaemonServer(TunnelController controller, string? socketPath = null, uint? targetUid = null)
    {
        _controller = controller;
        _socketPath = socketPath ?? DaemonClient.DefaultSocketPath;
        _targetUid = targetUid ?? GetSudoUid();
    }

    public async Task StartAsync()
    {
        var dir = Path.GetDirectoryName(_socketPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(_socketPath))
        {
            try { File.Delete(_socketPath); } catch { }
        }

        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        _listener.Listen(16);

        // Adjust ownership / permissions so the invoking user can access the socket
        if (OperatingSystem.IsLinux() && _targetUid.HasValue)
        {
            LinuxNative.Chown(_socketPath, _targetUid.Value, _targetUid.Value);
        }

        _acceptTask = Task.Run(AcceptLoopAsync);

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

    private async Task AcceptLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptAsync(_cts.Token).ConfigureAwait(false);
                _ = HandleClientAsync(client, _cts.Token);
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

    private async Task HandleClientAsync(Socket client, CancellationToken ct)
    {
        using (client)
        using (var stream = new NetworkStream(client, ownsSocket: false))
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
        if (File.Exists(_socketPath))
        {
            try { File.Delete(_socketPath); } catch { }
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

using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Daemon;

public sealed class DaemonClient
{
    public const string DefaultSocketPath = "/run/ergoproxy/control.sock";
    private readonly string _socketPath;

    public DaemonClient(string? socketPath = null)
    {
        _socketPath = socketPath ?? DefaultSocketPath;
    }

    public async Task<bool> IsRunningAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_socketPath)) return false;
        try
        {
            var status = await GetStatusAsync(ct).ConfigureAwait(false);
            return status != null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<TunnelStatus?> GetStatusAsync(CancellationToken ct = default)
    {
        var response = await SendCommandAsync(new ControlRequest { Command = "status" }, ct).ConfigureAwait(false);
        return response?.Status;
    }

    public async Task<ControlResponse> StartAsync(TunnelStartRequest startRequest, CancellationToken ct = default)
    {
        var response = await SendCommandAsync(new ControlRequest
        {
            Command = "start",
            StartRequest = startRequest
        }, ct).ConfigureAwait(false);

        return response ?? ControlResponse.Fail("No response received from daemon.");
    }

    public async Task<ControlResponse> StopAsync(CancellationToken ct = default)
    {
        var response = await SendCommandAsync(new ControlRequest { Command = "stop" }, ct).ConfigureAwait(false);
        return response ?? ControlResponse.Fail("No response received from daemon.");
    }

    public async Task<ControlResponse> ShutdownAsync(CancellationToken ct = default)
    {
        var response = await SendCommandAsync(new ControlRequest { Command = "shutdown" }, ct).ConfigureAwait(false);
        return response ?? ControlResponse.Ok("Daemon shutdown initiated.");
    }

    private async Task<ControlResponse?> SendCommandAsync(ControlRequest request, CancellationToken ct)
    {
        if (!File.Exists(_socketPath)) return null;

        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), ct).ConfigureAwait(false);

            using var stream = new NetworkStream(socket, ownsSocket: false);
            using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

            var json = JsonSerializer.Serialize(request);
            await writer.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
            await writer.FlushAsync(ct).ConfigureAwait(false);

            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line)) return null;

            return JsonSerializer.Deserialize<ControlResponse>(line);
        }
        catch
        {
            return null;
        }
    }
}

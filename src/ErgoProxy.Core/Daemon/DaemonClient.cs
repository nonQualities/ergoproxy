using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Daemon;

public sealed class DaemonClient
{
    public const string DefaultLinuxSocketPath = "/run/ergoproxy/control.sock";
    public const string DefaultWindowsPipeName = "ergoproxy-control";

    public static string DefaultEndpoint => OperatingSystem.IsWindows() ? DefaultWindowsPipeName : DefaultLinuxSocketPath;

    private readonly string _endpoint;

    public DaemonClient(string? endpoint = null) => _endpoint = endpoint ?? DefaultEndpoint;

    public async Task<bool> IsRunningAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(500));
            var status = await GetStatusAsync(cts.Token).ConfigureAwait(false);
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
        var response = await SendCommandAsync(new ControlRequest { Command = "start", StartRequest = startRequest }, ct).ConfigureAwait(false);
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
        Stream? stream = null;
        Socket? socket = null;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var pipe = new NamedPipeClientStream(".", _endpoint, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(500, ct).ConfigureAwait(false);
                stream = pipe;
            }
            else
            {
                if (!File.Exists(_endpoint)) return null;
                socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(_endpoint), ct).ConfigureAwait(false);
                stream = new NetworkStream(socket, ownsSocket: false);
            }

            using (stream)
            using (socket)
            using (var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true))
            using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
            {
                var json = JsonSerializer.Serialize(request);
                await writer.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false);

                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line)) return null;

                return JsonSerializer.Deserialize<ControlResponse>(line);
            }
        }
        catch
        {
            return null;
        }
    }
}

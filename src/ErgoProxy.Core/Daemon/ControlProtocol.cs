using System.Text.Json.Serialization;
using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Daemon;

public sealed class ControlRequest
{
    [JsonPropertyName("command")] public string Command { get; set; } = string.Empty;
    [JsonPropertyName("start_request")] public TunnelStartRequest? StartRequest { get; set; }
}

public sealed class ControlResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    [JsonPropertyName("status")] public TunnelStatus? Status { get; set; }

    public static ControlResponse Ok(string message, TunnelStatus? status = null) =>
        new() { Success = true, Message = message, Status = status };

    public static ControlResponse Fail(string message, TunnelStatus? status = null) =>
        new() { Success = false, Message = message, Status = status };
}

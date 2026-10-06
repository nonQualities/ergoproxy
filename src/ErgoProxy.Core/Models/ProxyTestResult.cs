using System.Text.Json.Serialization;

namespace ErgoProxy.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProxyTestStage
{
    Configuration,
    DnsResolution,
    TcpConnection,
    ProxyHandshake,
    ProxyAuth,
    ConnectTunnel,
    TlsHandshake,
    DestinationRequest
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProxyTestErrorCode
{
    None,
    InvalidConfig,
    DnsResolutionFailed,
    ConnectionRefused,
    ConnectionTimeout,
    AuthFailed,
    AccessDenied,
    UnsupportedProxyBehavior,
    TlsValidationFailed,
    HttpError,
    UnknownError
}

public sealed class ProxyTestResult
{
    [JsonPropertyName("is_success")]
    public bool IsSuccess { get; set; }

    [JsonPropertyName("stage")]
    public ProxyTestStage Stage { get; set; }

    [JsonPropertyName("error_code")]
    public ProxyTestErrorCode ErrorCode { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("latency_ms")]
    public double LatencyMilliseconds { get; set; }

    [JsonPropertyName("http_status_code")]
    public int? HttpStatusCode { get; set; }

    [JsonPropertyName("target_url")]
    public string? TargetUrl { get; set; }

    [JsonPropertyName("connect_tunnel_success")]
    public bool ConnectTunnelSuccess { get; set; }

    [JsonPropertyName("tested_at")]
    public DateTimeOffset TestedAt { get; set; } = DateTimeOffset.UtcNow;

    public static ProxyTestResult Success(string message, double latencyMs, string targetUrl, bool connectTunnelSuccess = false, int? statusCode = 200)
    {
        return new ProxyTestResult
        {
            IsSuccess = true,
            Stage = connectTunnelSuccess ? ProxyTestStage.ConnectTunnel : ProxyTestStage.DestinationRequest,
            ErrorCode = ProxyTestErrorCode.None,
            Message = message,
            LatencyMilliseconds = latencyMs,
            HttpStatusCode = statusCode,
            TargetUrl = targetUrl,
            ConnectTunnelSuccess = connectTunnelSuccess
        };
    }

    public static ProxyTestResult Failure(ProxyTestStage stage, ProxyTestErrorCode errorCode, string message, double latencyMs = 0, int? statusCode = null, string? targetUrl = null)
    {
        return new ProxyTestResult
        {
            IsSuccess = false,
            Stage = stage,
            ErrorCode = errorCode,
            Message = message,
            LatencyMilliseconds = latencyMs,
            HttpStatusCode = statusCode,
            TargetUrl = targetUrl,
            ConnectTunnelSuccess = false
        };
    }

    public override string ToString()
    {
        return IsSuccess 
            ? $"[SUCCESS] {Message} ({LatencyMilliseconds:F1}ms)" 
            : $"[FAILED] ({Stage} / {ErrorCode}) {Message}";
    }
}

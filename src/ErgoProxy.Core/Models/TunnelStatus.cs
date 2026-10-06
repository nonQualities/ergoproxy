using System.Text.Json.Serialization;

namespace ErgoProxy.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<TunnelState>))]
public enum TunnelState
{
    Disconnected,
    Validating,
    Connecting,
    Connected,
    /// <summary>Tunnel is up but the upstream proxy is currently failing health probes. Traffic is held, not leaked.</summary>
    Degraded,
    Disconnecting,
    Error
}

[JsonConverter(typeof(JsonStringEnumConverter<TunnelErrorKind>))]
public enum TunnelErrorKind
{
    None,
    InvalidConfig,
    MissingCredentials,
    ProxyDnsFailed,
    ProxyUnreachable,
    ProxyTimeout,
    AuthRequired,
    AuthFailed,
    PolicyDenied,
    DestinationFailed,
    MalformedProxyResponse,
    TunnelFailed,
    PlatformUnsupported,
    PermissionDenied,
    DaemonUnavailable,
    Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter<SupportLevel>))]
public enum SupportLevel
{
    Supported,
    Partial,
    Blocked,
    Unsupported
}

public sealed class ProtocolSupport
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("level")] public SupportLevel Level { get; set; }
    [JsonPropertyName("note")] public string Note { get; set; } = string.Empty;

    public static List<ProtocolSupport> Phase1Matrix() =>
    [
        new() { Name = "TCP / IPv4", Level = SupportLevel.Supported, Note = "Every TCP flow is tunnelled with HTTP CONNECT (TLS untouched)." },
        new() { Name = "HTTP (port 80)", Level = SupportLevel.Supported, Note = "Sent as absolute-URI proxy requests when CONNECT :80 is not allowed." },
        new() { Name = "DNS", Level = SupportLevel.Supported, Note = "Fake-IP resolver; hostnames are resolved by the proxy. Other record types use DoH via the proxy." },
        new() { Name = "UDP (QUIC, games, VoIP)", Level = SupportLevel.Blocked, Note = "HTTP proxies cannot carry UDP; rejected with ICMP so apps fall back to TCP." },
        new() { Name = "IPv6", Level = SupportLevel.Blocked, Note = "Captured and rejected to prevent leaks; applications fall back to IPv4." },
        new() { Name = "ICMP (ping)", Level = SupportLevel.Unsupported, Note = "Not transportable over an HTTP proxy." },
    ];
}

public sealed class TunnelStats
{
    [JsonInclude, JsonPropertyName("total_connections")] public long TotalConnections;
    [JsonInclude, JsonPropertyName("active_connections")] public long ActiveConnections;
    [JsonInclude, JsonPropertyName("failed_connections")] public long FailedConnections;
    [JsonInclude, JsonPropertyName("direct_connections")] public long DirectConnections;
    [JsonInclude, JsonPropertyName("bytes_up")] public long BytesUp;
    [JsonInclude, JsonPropertyName("bytes_down")] public long BytesDown;
    [JsonInclude, JsonPropertyName("dns_queries")] public long DnsQueries;
    [JsonInclude, JsonPropertyName("udp_rejected")] public long UdpRejected;
    [JsonInclude, JsonPropertyName("ipv6_rejected")] public long Ipv6Rejected;
    [JsonInclude, JsonPropertyName("proxy_auth_failures")] public long ProxyAuthFailures;
    [JsonInclude, JsonPropertyName("proxy_policy_denials")] public long ProxyPolicyDenials;
    [JsonInclude, JsonPropertyName("destination_failures")] public long DestinationFailures;
    [JsonInclude, JsonPropertyName("packets_in")] public long PacketsIn;
    [JsonInclude, JsonPropertyName("packets_out")] public long PacketsOut;
}

public sealed class TunnelStatus
{
    [JsonPropertyName("state")] public TunnelState State { get; set; } = TunnelState.Disconnected;
    [JsonPropertyName("error_kind")] public TunnelErrorKind ErrorKind { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "Tunnel is not running.";
    [JsonPropertyName("profile_name")] public string? ProfileName { get; set; }
    [JsonPropertyName("proxy_endpoint")] public string? ProxyEndpoint { get; set; }
    [JsonPropertyName("authentication")] public bool Authentication { get; set; }
    [JsonPropertyName("interface_name")] public string? InterfaceName { get; set; }
    [JsonPropertyName("dns_mode")] public string? DnsMode { get; set; }
    [JsonPropertyName("connected_since")] public DateTimeOffset? ConnectedSince { get; set; }
    [JsonPropertyName("upstream_reachable")] public bool UpstreamReachable { get; set; }
    [JsonPropertyName("last_probe_at")] public DateTimeOffset? LastProbeAt { get; set; }
    [JsonPropertyName("last_probe_latency_ms")] public double? LastProbeLatencyMs { get; set; }
    [JsonPropertyName("last_probe_message")] public string? LastProbeMessage { get; set; }
    [JsonPropertyName("stats")] public TunnelStats Stats { get; set; } = new();
    [JsonPropertyName("protocols")] public List<ProtocolSupport> Protocols { get; set; } = ProtocolSupport.Phase1Matrix();
    [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = new();

    public bool IsActive => State is TunnelState.Connected or TunnelState.Degraded or TunnelState.Connecting or TunnelState.Validating;

    public static TunnelStatus NotRunning(string? message = null) => new()
    {
        State = TunnelState.Disconnected,
        Message = message ?? "Tunnel is not running."
    };
}

/// <summary>Everything the privileged daemon needs to start the tunnel. Credentials are never persisted or logged.</summary>
public sealed class TunnelStartRequest
{
    [JsonPropertyName("profile_name")] public string ProfileName { get; set; } = string.Empty;
    [JsonPropertyName("proxy_host")] public string ProxyHost { get; set; } = string.Empty;
    [JsonPropertyName("proxy_port")] public int ProxyPort { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("password")] public string? Password { get; set; }
    [JsonPropertyName("bypass_rules")] public List<string> BypassRules { get; set; } = new();
    [JsonPropertyName("probe_host")] public string ProbeHost { get; set; } = "www.google.com";
    [JsonPropertyName("probe_port")] public int ProbePort { get; set; } = 443;
    [JsonPropertyName("doh_server")] public string DohServer { get; set; } = "cloudflare-dns.com";

    public ProxyCredentials? Credentials =>
        string.IsNullOrEmpty(Username) ? null : new ProxyCredentials(Username, Password ?? string.Empty);

    public override string ToString() =>
        $"{ProfileName} -> {ProxyHost}:{ProxyPort} (auth: {(string.IsNullOrEmpty(Username) ? "none" : $"user '{Username}'")})";
}

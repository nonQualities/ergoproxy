using System.Text.Json.Serialization;

namespace ErgoProxy.Core.Models;

public sealed class SystemProxyConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("http_host")]
    public string? HttpHost { get; set; }

    [JsonPropertyName("http_port")]
    public int? HttpPort { get; set; }

    [JsonPropertyName("https_host")]
    public string? HttpsHost { get; set; }

    [JsonPropertyName("https_port")]
    public int? HttpsPort { get; set; }

    [JsonPropertyName("bypass_rules")]
    public List<string> BypassRules { get; set; } = new();

    [JsonPropertyName("source_platform")]
    public string? SourcePlatform { get; set; }

    [JsonPropertyName("raw_settings")]
    public Dictionary<string, string> RawSettings { get; set; } = new();

    [JsonPropertyName("captured_at")]
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsEquivalentTo(string host, int port)
    {
        return Enabled && 
               string.Equals(HttpHost, host, StringComparison.OrdinalIgnoreCase) && 
               HttpPort == port;
    }
}

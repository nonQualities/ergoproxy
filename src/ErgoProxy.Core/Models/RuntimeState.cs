using System.Text.Json.Serialization;

namespace ErgoProxy.Core.Models;

public sealed class RuntimeState
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("active_profile_id")]
    public string? ActiveProfileId { get; set; }

    [JsonPropertyName("is_system_proxy_applied")]
    public bool IsSystemProxyApplied { get; set; }

    [JsonPropertyName("applied_endpoint")]
    public string? AppliedEndpoint { get; set; }

    [JsonPropertyName("restorable_configuration")]
    public SystemProxyConfiguration? RestorableConfiguration { get; set; }

    [JsonPropertyName("last_test_result")]
    public ProxyTestResult? LastTestResult { get; set; }

    [JsonPropertyName("last_tested_at")]
    public DateTimeOffset? LastTestedAt { get; set; }

    [JsonPropertyName("has_pending_recovery")]
    public bool HasPendingRecovery { get; set; }

    [JsonPropertyName("recovery_message")]
    public string? RecoveryMessage { get; set; }
}

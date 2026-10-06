using System.Text.Json.Serialization;

namespace ErgoProxy.Core.Models;

public sealed class ProxyProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("D");

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("host")]
    public string Host { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; } = 8080;

    [JsonPropertyName("authentication_enabled")]
    public bool AuthenticationEnabled { get; set; }

    [JsonPropertyName("credential_reference")]
    public string? CredentialReference { get; set; }

    [JsonPropertyName("bypass_rules")]
    public List<string> BypassRules { get; set; } = new();

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("modified_at")]
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;

    public ProxyProfile Clone()
    {
        return new ProxyProfile
        {
            Id = Id,
            Name = Name,
            Host = Host,
            Port = Port,
            AuthenticationEnabled = AuthenticationEnabled,
            CredentialReference = CredentialReference,
            BypassRules = new List<string>(BypassRules),
            CreatedAt = CreatedAt,
            ModifiedAt = ModifiedAt
        };
    }

    public override string ToString()
    {
        var authStatus = AuthenticationEnabled ? "Auth:Enabled" : "Auth:Disabled";
        return $"{Name} ({Host}:{Port}, {authStatus}) [{Id}]";
    }
}

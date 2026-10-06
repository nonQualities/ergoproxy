using System.Net;
using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Validation;

public sealed class ProfileValidator : IProfileValidator
{
    public ValidationResult Validate(ProxyProfile profile)
    {
        var errors = new List<string>();

        if (profile == null)
        {
            return ValidationResult.Failure("Profile cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            errors.Add("Profile ID cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            errors.Add("Profile name cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(profile.Host))
        {
            errors.Add("Proxy host cannot be empty.");
        }
        else if (!IsValidHost(profile.Host.Trim()))
        {
            errors.Add($"Proxy host '{profile.Host}' is not a valid hostname or IP address.");
        }

        if (profile.Port is < 1 or > 65535)
        {
            errors.Add($"Proxy port {profile.Port} is invalid. Port must be between 1 and 65535.");
        }

        if (profile.AuthenticationEnabled && string.IsNullOrWhiteSpace(profile.CredentialReference))
        {
            errors.Add("Credential reference must be specified when authentication is enabled.");
        }

        if (profile.BypassRules != null)
        {
            for (var i = 0; i < profile.BypassRules.Count; i++)
            {
                var rule = profile.BypassRules[i];
                if (string.IsNullOrWhiteSpace(rule))
                {
                    errors.Add($"Bypass rule at index {i} cannot be empty.");
                }
            }
        }

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }

    public ValidationResult ValidateUniqueId(ProxyProfile profile, IEnumerable<ProxyProfile> existingProfiles)
    {
        if (profile == null || string.IsNullOrWhiteSpace(profile.Id))
        {
            return ValidationResult.Failure("Profile ID cannot be empty.");
        }

        var duplicate = existingProfiles.Any(p => 
            string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));

        return duplicate 
            ? ValidationResult.Failure($"A profile with ID '{profile.Id}' already exists.") 
            : ValidationResult.Success();
    }

    private static bool IsValidHost(string host)
    {
        if (IPAddress.TryParse(host, out _))
        {
            return true;
        }

        // Hostname syntax check (RFC 1123)
        var uriHostType = Uri.CheckHostName(host);
        return uriHostType is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
    }
}

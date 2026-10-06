using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Validation;

public interface IProfileValidator
{
    ValidationResult Validate(ProxyProfile profile);
    ValidationResult ValidateUniqueId(ProxyProfile profile, IEnumerable<ProxyProfile> existingProfiles);
}

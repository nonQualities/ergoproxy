using ErgoProxy.Core.Models;
using ErgoProxy.Core.Validation;
using Xunit;

namespace ErgoProxy.Tests.Core;

public class ProfileValidatorTests
{
    private readonly ProfileValidator _validator = new();

    [Fact]
    public void Validate_ValidProfile_ReturnsSuccess()
    {
        var profile = new ProxyProfile
        {
            Id = "p1",
            Name = "Campus Proxy",
            Host = "proxy.college.edu",
            Port = 8080,
            AuthenticationEnabled = false,
            BypassRules = new List<string> { "localhost", "127.0.0.1" }
        };

        var result = _validator.Validate(profile);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(99999)]
    public void Validate_InvalidPort_ReturnsError(int port)
    {
        var profile = new ProxyProfile
        {
            Id = "p1",
            Name = "Test",
            Host = "127.0.0.1",
            Port = port
        };

        var result = _validator.Validate(profile);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Port must be between 1 and 65535"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_MissingHost_ReturnsError(string? host)
    {
        var profile = new ProxyProfile
        {
            Id = "p1",
            Name = "Test",
            Host = host!,
            Port = 8080
        };

        var result = _validator.Validate(profile);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Proxy host cannot be empty"));
    }

    [Fact]
    public void Validate_AuthEnabledWithoutCredentialReference_ReturnsError()
    {
        var profile = new ProxyProfile
        {
            Id = "p1",
            Name = "Test",
            Host = "127.0.0.1",
            Port = 8080,
            AuthenticationEnabled = true,
            CredentialReference = null
        };

        var result = _validator.Validate(profile);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Credential reference must be specified"));
    }

    [Fact]
    public void ValidateUniqueId_DuplicateId_ReturnsError()
    {
        var existing = new List<ProxyProfile>
        {
            new() { Id = "profile-1", Name = "Existing", Host = "127.0.0.1", Port = 8080 }
        };

        var newProfile = new ProxyProfile
        {
            Id = "profile-1",
            Name = "New with same ID",
            Host = "10.0.0.1",
            Port = 3128
        };

        var result = _validator.ValidateUniqueId(newProfile, existing);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("already exists"));
    }
}

using ErgoProxy.Core.Security;
using Xunit;

namespace ErgoProxy.Tests.Core;

public class SecretRedactorTests
{
    [Fact]
    public void Redact_UrlWithCredentials_MasksPassword()
    {
        var url = "http://alice:SuperSecretPassword123@proxy.example.com:8080/path";
        var redacted = SecretRedactor.Redact(url);

        Assert.DoesNotContain("SuperSecretPassword123", redacted);
        Assert.Contains("alice:***@proxy.example.com", redacted);
    }

    [Fact]
    public void Redact_ProxyAuthorizationHeader_MasksToken()
    {
        var header = "Proxy-Authorization: Basic dXNlcjpwYXNzd29yZA==";
        var redacted = SecretRedactor.Redact(header);

        Assert.DoesNotContain("dXNlcjpwYXNzd29yZA==", redacted);
        Assert.Contains("Proxy-Authorization: Basic ***", redacted);
    }

    [Fact]
    public void Redact_CliPasswordParam_MasksPassword()
    {
        var cli = "ergoproxy configure --name Work --password MySecretPass --host 10.0.0.1";
        var redacted = SecretRedactor.Redact(cli);

        Assert.DoesNotContain("MySecretPass", redacted);
        Assert.Contains("--password ***", redacted);
    }

    [Fact]
    public void Redact_JsonWithPassword_MasksPassword()
    {
        var json = "{\"username\": \"john\", \"password\": \"TopSecretVal!\"}";
        var redacted = SecretRedactor.Redact(json);

        Assert.DoesNotContain("TopSecretVal!", redacted);
        Assert.Contains("\"password\": \"***\"", redacted);
    }
}

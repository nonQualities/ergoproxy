using ErgoProxy.Core.Credentials;
using ErgoProxy.Core.Models;
using Xunit;

namespace ErgoProxy.Tests.Core;

public class EncryptedFileCredentialStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _vaultPath;
    private readonly EncryptedFileCredentialStore _store;

    public EncryptedFileCredentialStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ergoproxy_test_vault_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _vaultPath = Path.Combine(_tempDir, "vault.enc");
        _store = new EncryptedFileCredentialStore(_vaultPath);
    }

    [Fact]
    public async Task SaveAndGetCredentials_ReturnsDecryptedCredentials()
    {
        var creds = new ProxyCredentials("user1", "P@ssw0rd!Secure");
        await _store.SaveCredentialsAsync("ref-1", creds);

        var retrieved = await _store.GetCredentialsAsync("ref-1");
        Assert.NotNull(retrieved);
        Assert.Equal("user1", retrieved.Username);
        Assert.Equal("P@ssw0rd!Secure", retrieved.Password);
    }

    [Fact]
    public async Task Credentials_AtRest_AreEncrypted()
    {
        var creds = new ProxyCredentials("secretuser", "PlaintextSecretMustNotAppear");
        await _store.SaveCredentialsAsync("ref-2", creds);

        Assert.True(File.Exists(_vaultPath));
        var rawBytes = await File.ReadAllBytesAsync(_vaultPath);
        var rawText = System.Text.Encoding.UTF8.GetString(rawBytes);

        // Password must not be plaintext in the file
        Assert.DoesNotContain("PlaintextSecretMustNotAppear", rawText);
    }

    [Fact]
    public async Task DeleteCredentials_RemovesCredential()
    {
        var creds = new ProxyCredentials("user3", "secret3");
        await _store.SaveCredentialsAsync("ref-3", creds);

        Assert.True(await _store.ExistsAsync("ref-3"));

        var deleted = await _store.DeleteCredentialsAsync("ref-3");
        Assert.True(deleted);

        var retrieved = await _store.GetCredentialsAsync("ref-3");
        Assert.Null(retrieved);
        Assert.False(await _store.ExistsAsync("ref-3"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }
}

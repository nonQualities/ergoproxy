using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Credentials;

public sealed class EncryptedFileCredentialStore : ICredentialStore
{
    private const int KeySize = 32; // 256-bit AES
    private const int NonceSize = 12; // 96-bit GCM nonce
    private const int TagSize = 16; // 128-bit auth tag
    private const int SaltSize = 16;
    private const int Pbkdf2Iterations = 100_000;

    private readonly string _vaultFilePath;
    private readonly byte[] _masterKey;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string StoreName => "EncryptedFileVault";

    public EncryptedFileCredentialStore(string? vaultFilePath = null)
    {
        _vaultFilePath = vaultFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "ergoproxy", "vault.enc");

        _masterKey = DeriveMasterKey();
    }

    private static byte[] DeriveMasterKey()
    {
        // Derive key from unique machine and user context
        var machineId = Environment.MachineName;
        var userName = Environment.UserName;
        var osVersion = Environment.OSVersion.ToString();
        var entropySeed = $"ErgoProxy::CredentialKey::{machineId}::{userName}::{osVersion}";

        // Salt derived from fixed application identifier + machine seed
        var salt = SHA256.HashData(Encoding.UTF8.GetBytes($"ergoproxy_salt::{userName}"))[..SaltSize];

        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(entropySeed),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeySize);
    }

    public async Task SaveCredentialsAsync(string credentialReference, ProxyCredentials credentials, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialReference);
        ArgumentNullException.ThrowIfNull(credentials);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var store = await LoadStoreInternalAsync(ct).ConfigureAwait(false);
            store[credentialReference] = credentials;
            await SaveStoreInternalAsync(store, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ProxyCredentials?> GetCredentialsAsync(string credentialReference, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialReference);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var store = await LoadStoreInternalAsync(ct).ConfigureAwait(false);
            return store.TryGetValue(credentialReference, out var creds) ? creds : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteCredentialsAsync(string credentialReference, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialReference);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var store = await LoadStoreInternalAsync(ct).ConfigureAwait(false);
            var removed = store.Remove(credentialReference);
            if (removed)
            {
                await SaveStoreInternalAsync(store, ct).ConfigureAwait(false);
            }
            return removed;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> ExistsAsync(string credentialReference, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialReference);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var store = await LoadStoreInternalAsync(ct).ConfigureAwait(false);
            return store.ContainsKey(credentialReference);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Dictionary<string, ProxyCredentials>> LoadStoreInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(_vaultFilePath))
        {
            return new Dictionary<string, ProxyCredentials>(StringComparer.OrdinalIgnoreCase);
        }

        var encryptedBytes = await File.ReadAllBytesAsync(_vaultFilePath, ct).ConfigureAwait(false);
        if (encryptedBytes.Length < NonceSize + TagSize)
        {
            return new Dictionary<string, ProxyCredentials>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var nonce = encryptedBytes.AsSpan(0, NonceSize);
            var tag = encryptedBytes.AsSpan(NonceSize, TagSize);
            var cipherLength = encryptedBytes.Length - NonceSize - TagSize;
            var ciphertext = encryptedBytes.AsSpan(NonceSize + TagSize, cipherLength);

            var plaintext = new byte[cipherLength];
            using var aesGcm = new AesGcm(_masterKey, TagSize);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

            var json = Encoding.UTF8.GetString(plaintext);
            var result = JsonSerializer.Deserialize<Dictionary<string, ProxyCredentials>>(json);
            return result ?? new Dictionary<string, ProxyCredentials>(StringComparer.OrdinalIgnoreCase);
        }
        catch (CryptographicException)
        {
            // Tampered or corrupted vault
            return new Dictionary<string, ProxyCredentials>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task SaveStoreInternalAsync(Dictionary<string, ProxyCredentials> store, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_vaultFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(store);
        var plaintext = Encoding.UTF8.GetBytes(json);

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aesGcm = new AesGcm(_masterKey, TagSize))
        {
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var payload = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, payload, NonceSize + TagSize, ciphertext.Length);

        var tempPath = _vaultFilePath + ".tmp";
        await File.WriteAllBytesAsync(tempPath, payload, ct).ConfigureAwait(false);
        File.Move(tempPath, _vaultFilePath, overwrite: true);
    }
}

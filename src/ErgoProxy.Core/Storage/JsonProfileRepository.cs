using System.Text.Json;
using System.Text.Json.Serialization;
using ErgoProxy.Core.Models;
using ErgoProxy.Core.Validation;

namespace ErgoProxy.Core.Storage;

public sealed class ProfileDocument
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("profiles")]
    public List<ProxyProfile> Profiles { get; set; } = new();

    [JsonPropertyName("last_updated")]
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class JsonProfileRepository : IProfileRepository
{
    private readonly string _filePath;
    private readonly IProfileValidator _validator;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public JsonProfileRepository(string? filePath = null, IProfileValidator? validator = null)
    {
        _validator = validator ?? new ProfileValidator();
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "ergoproxy", "profiles.json");
    }

    public async Task<IReadOnlyList<ProxyProfile>> GetAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var doc = await LoadDocumentInternalAsync(ct).ConfigureAwait(false);
            return doc.Profiles.Select(p => p.Clone()).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ProxyProfile?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var doc = await LoadDocumentInternalAsync(ct).ConfigureAwait(false);
            var found = doc.Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            return found?.Clone();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(ProxyProfile profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var valResult = _validator.Validate(profile);
        if (!valResult.IsValid)
        {
            throw new ArgumentException($"Invalid profile: {string.Join("; ", valResult.Errors)}");
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var doc = await LoadDocumentInternalAsync(ct).ConfigureAwait(false);
            var index = doc.Profiles.FindIndex(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));

            profile.ModifiedAt = DateTimeOffset.UtcNow;

            if (index >= 0)
            {
                // Update
                profile.CreatedAt = doc.Profiles[index].CreatedAt;
                doc.Profiles[index] = profile.Clone();
            }
            else
            {
                // Check duplicate ID
                var uniq = _validator.ValidateUniqueId(profile, doc.Profiles);
                if (!uniq.IsValid)
                {
                    throw new InvalidOperationException(string.Join("; ", uniq.Errors));
                }
                doc.Profiles.Add(profile.Clone());
            }

            doc.LastUpdated = DateTimeOffset.UtcNow;
            await SaveDocumentInternalAsync(doc, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var doc = await LoadDocumentInternalAsync(ct).ConfigureAwait(false);
            var removedCount = doc.Profiles.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (removedCount > 0)
            {
                doc.LastUpdated = DateTimeOffset.UtcNow;
                await SaveDocumentInternalAsync(doc, ct).ConfigureAwait(false);
                return true;
            }
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<ProfileDocument> LoadDocumentInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            return new ProfileDocument();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ProfileDocument();
            }

            var doc = JsonSerializer.Deserialize<ProfileDocument>(json, JsonOptions);
            return doc ?? new ProfileDocument();
        }
        catch (JsonException ex)
        {
            // Handle corrupted file: backup to .corrupted.<timestamp> so user data is never lost
            var backupPath = $"{_filePath}.corrupted.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            try
            {
                File.Copy(_filePath, backupPath, overwrite: true);
            }
            catch
            {
                // ignore backup failure
            }

            throw new InvalidOperationException(
                $"Corrupted profile configuration detected at '{_filePath}'. A backup copy was preserved at '{backupPath}'. Original error: {ex.Message}", ex);
        }
    }

    private async Task SaveDocumentInternalAsync(ProfileDocument doc, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(doc, JsonOptions);
        var tempFile = $"{_filePath}.tmp";

        await File.WriteAllTextAsync(tempFile, json, ct).ConfigureAwait(false);
        File.Move(tempFile, _filePath, overwrite: true);
    }
}

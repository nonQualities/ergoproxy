using System.Text.Json;
using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Storage;

public sealed class JsonStateManager : IStateManager
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public JsonStateManager(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "ergoproxy", "state.json");
    }

    public async Task<RuntimeState> GetStateAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                return new RuntimeState();
            }

            var json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new RuntimeState();
            }

            var state = JsonSerializer.Deserialize<RuntimeState>(json, JsonOptions);
            return state ?? new RuntimeState();
        }
        catch
        {
            return new RuntimeState();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveStateAsync(RuntimeState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(state, JsonOptions);
            var tempFile = $"{_filePath}.tmp";

            await File.WriteAllTextAsync(tempFile, json, ct).ConfigureAwait(false);
            File.Move(tempFile, _filePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearStateAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}

using ErgoProxy.Core.Models;
using ErgoProxy.Core.Storage;
using Xunit;

namespace ErgoProxy.Tests.Core;

public class JsonProfileRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _profilesPath;
    private readonly JsonProfileRepository _repo;

    public JsonProfileRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ergoproxy_test_repo_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _profilesPath = Path.Combine(_tempDir, "profiles.json");
        _repo = new JsonProfileRepository(_profilesPath);
    }

    [Fact]
    public async Task SaveAsync_PersistsProfile()
    {
        var profile = new ProxyProfile
        {
            Id = "prof-1",
            Name = "Library Proxy",
            Host = "proxy.lib.edu",
            Port = 3128,
            BypassRules = new List<string> { "localhost" }
        };

        await _repo.SaveAsync(profile);

        var retrieved = await _repo.GetByIdAsync("prof-1");
        Assert.NotNull(retrieved);
        Assert.Equal("Library Proxy", retrieved.Name);
        Assert.Equal("proxy.lib.edu", retrieved.Host);
        Assert.Equal(3128, retrieved.Port);

        var all = await _repo.GetAllAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task CorruptedFile_CreatesBackup_AndDoesNotCrashSilently()
    {
        // Write corrupted JSON to file
        await File.WriteAllTextAsync(_profilesPath, "{ invalid json content: 1234 }");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _repo.GetAllAsync());
        Assert.Contains("Corrupted profile configuration detected", ex.Message);

        // Verify backup was made
        var backups = Directory.GetFiles(_tempDir, "profiles.json.corrupted.*");
        Assert.NotEmpty(backups);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }
}

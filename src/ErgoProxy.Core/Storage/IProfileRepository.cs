using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Storage;

public interface IProfileRepository
{
    Task<IReadOnlyList<ProxyProfile>> GetAllAsync(CancellationToken ct = default);
    Task<ProxyProfile?> GetByIdAsync(string id, CancellationToken ct = default);
    Task SaveAsync(ProxyProfile profile, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}

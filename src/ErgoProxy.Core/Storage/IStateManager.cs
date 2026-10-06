using ErgoProxy.Core.Models;

namespace ErgoProxy.Core.Storage;

public interface IStateManager
{
    Task<RuntimeState> GetStateAsync(CancellationToken ct = default);
    Task SaveStateAsync(RuntimeState state, CancellationToken ct = default);
    Task ClearStateAsync(CancellationToken ct = default);
}

using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Loot.Admin;

namespace Vortex.Primitives.Loot;

/// <summary>
/// Admin writes for the prize pools every reward furniture draws from. Every write reloads the
/// kept-alive cache in <see cref="Grains.ILootTableManagerGrain"/>, so an edit is live without a
/// restart.
/// </summary>
public interface ILootTableAdminService
{
    public Task<LootAdminResult> CreatePoolAsync(LootTableSpec spec, CancellationToken ct);

    public Task<LootAdminResult> UpdatePoolAsync(
        int poolId,
        LootTableSpec spec,
        CancellationToken ct
    );

    /// <summary>Deletes a pool and, by cascade, every entry weighted against it.</summary>
    public Task<LootAdminResult> DeletePoolAsync(int poolId, CancellationToken ct);

    public Task<LootAdminResult> CreateEntryAsync(LootEntrySpec spec, CancellationToken ct);

    public Task<LootAdminResult> UpdateEntryAsync(
        int entryId,
        LootEntrySpec spec,
        CancellationToken ct
    );

    public Task<LootAdminResult> DeleteEntryAsync(int entryId, CancellationToken ct);

    /// <summary>Points a furniture definition at the pool it draws from.</summary>
    public Task<LootAdminResult> CreateBindingAsync(LootBindingSpec spec, CancellationToken ct);

    public Task<LootAdminResult> UpdateBindingAsync(
        int bindingId,
        LootBindingSpec spec,
        CancellationToken ct
    );

    public Task<LootAdminResult> DeleteBindingAsync(int bindingId, CancellationToken ct);

    /// <summary>Rebuilds the live cache without changing anything.</summary>
    public Task<LootAdminResult> ReloadCacheAsync(CancellationToken ct);
}

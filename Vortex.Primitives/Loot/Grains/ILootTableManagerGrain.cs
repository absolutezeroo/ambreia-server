using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Loot.Snapshots;

namespace Vortex.Primitives.Loot.Grains;

/// <summary>
/// Singleton grain caching every prize pool and its weighted entries. Pools are read on each draw,
/// so re-querying them per open would put a table scan on the hot path for data that changes only
/// when an admin edits it.
/// </summary>
public interface ILootTableManagerGrain : IGrainWithStringKey
{
    /// <summary>
    /// Draws from the pool identified by <paramref name="poolCode"/>, restricted to
    /// <paramref name="variant"/> (empty when the pool is not variant-keyed). Null when the pool is
    /// missing, disabled, or holds nothing eligible.
    /// </summary>
    public Task<LootEntrySnapshot?> PickAsync(
        string poolCode,
        string variant,
        CancellationToken ct
    );

    /// <summary>
    /// The pool a furniture definition draws from, or null when it is bound to none — which is what
    /// makes a crackable that nobody configured inert rather than free furniture.
    /// </summary>
    public Task<LootBindingSnapshot?> GetBindingAsync(
        int furnitureDefinitionId,
        CancellationToken ct
    );

    /// <summary>Re-reads the tables into the cache, so admin edits go live without a restart.</summary>
    public Task ReloadAsync(CancellationToken ct);
}

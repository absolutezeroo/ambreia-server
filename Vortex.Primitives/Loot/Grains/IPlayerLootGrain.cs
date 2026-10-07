using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Loot.Snapshots;

namespace Vortex.Primitives.Loot.Grains;

/// <summary>
/// Hands a drawn prize to one player. Per-player so grants serialize on the player's own grain turn
/// — two furniture opening at once cannot interleave a club extension with an effect grant.
///
/// Every reward furniture ends here: the trigger decides <em>when</em> and the pool decides
/// <em>what</em>, but the granting and its audit trail are the same everywhere.
/// </summary>
public interface IPlayerLootGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// Grants <paramref name="entry"/> and returns what the reward window should draw, or null when
    /// nothing could be granted (unknown definition, malformed parameters, a product type the client
    /// cannot render). <paramref name="source"/> names the trigger for the audit trail — the pool
    /// alone does not say whether a prize came from a box, a crackable or a staff grant.
    /// </summary>
    public Task<LootAward?> GrantAsync(
        LootEntrySnapshot entry,
        string source,
        CancellationToken ct
    );

    /// <summary>
    /// Same, but only the first time this player draws from <paramref name="poolId"/>. Returns null
    /// when they already have — which is what lets a welcome gift stay in the room after paying out
    /// instead of being consumed like a box.
    /// </summary>
    public Task<LootAward?> GrantOnceAsync(
        LootEntrySnapshot entry,
        int poolId,
        string source,
        CancellationToken ct
    );
}

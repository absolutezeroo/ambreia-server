using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;
using Vortex.Primitives.Badges.Snapshots;

namespace Vortex.Primitives.Badges.Grains;

/// <summary>
/// The hotel's badge figures in one grain: how many players hold each code, and the rarity that
/// follows from it.
/// </summary>
/// <remarks>
/// Every badge drawn anywhere — the inventory list, an avatar's five worn badges, a profile, the
/// badge display furni — needs these two numbers, so they are counted once here instead of joined
/// into each of those queries. It is a read-through cache over <c>player_badges</c>: the rows are
/// the truth, and the counts are rebuilt on activation and again on a timer. Nothing is written
/// back, so there is nothing to flush.
/// <para>
/// There is deliberately no "a badge was granted" call. Badges are handed out from the inventory,
/// from the dashboard and from the achievement path, and a cache that each of those has to remember
/// to poke is a cache that goes wrong the first time somebody adds a fourth grant site. The recount
/// is the single refresh path, and the cost is that a brand-new badge carries the hotel's previous
/// figures for at most one refresh interval — which for a hotel-wide owner count is behind, not
/// wrong.
/// </para>
/// </remarks>
public interface IBadgeDirectoryGrain : IGrainWithStringKey
{
    /// <summary>The figures for the given codes, in the order asked. An unknown code answers zero.</summary>
    [ReadOnly]
    Task<ImmutableArray<BadgeInfoSnapshot>> GetInfoAsync(
        IReadOnlyList<string> badgeCodes,
        CancellationToken ct
    );

    /// <summary>The figures for one code. Shorthand for the single-badge callers.</summary>
    [ReadOnly]
    Task<BadgeInfoSnapshot> GetInfoAsync(string badgeCode, CancellationToken ct);

    /// <summary>
    /// How many badges of each tier a set of codes adds up to, which is the shape the extended
    /// profile puts on the wire. Tiers with no badge are left out.
    /// </summary>
    [ReadOnly]
    Task<ImmutableArray<BadgeRarityCountSnapshot>> CountByRarityAsync(
        IReadOnlyList<string> badgeCodes,
        CancellationToken ct
    );

    /// <summary>
    /// One chunk of a badge leaderboard, with the asking player's own row when they are on it.
    /// </summary>
    /// <remarks>
    /// <c>[ReadOnly]</c> for the same reason the cheap reads above are: this one actually queries
    /// the database, and every badge the hotel draws waits on this grain. Without it a board being
    /// built would hold up every avatar walking into a room.
    /// </remarks>
    [ReadOnly]
    Task<BadgeLeaderboardPageSnapshot> GetLeaderboardAsync(
        BadgeLeaderboardType type,
        int rarity,
        int chunkIndex,
        int chunkSize,
        int askingPlayerId,
        CancellationToken ct
    );
}

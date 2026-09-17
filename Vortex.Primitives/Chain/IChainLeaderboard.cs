using System;
using System.Collections.Immutable;
using System.Numerics;

namespace Vortex.Primitives.Chain;

/// <summary>One player's standing in the coin contest.</summary>
/// <param name="PlayerId">Who, so a caller can link to the profile.</param>
/// <param name="PlayerName">Their name as it was when the snapshot was taken.</param>
/// <param name="Address">The Stardust address the balance was read from.</param>
/// <param name="Balance">
/// VortexCoin held, in wei. Kept as the contract stores it: eighteen decimals is a display decision,
/// and rounding belongs where the number is shown, not where it is read.
/// </param>
public readonly record struct ChainLeaderboardEntry(
    int PlayerId,
    string PlayerName,
    string Address,
    BigInteger Balance
);

/// <summary>
/// Who holds the most VortexCoin — the contest the chain was built for.
/// </summary>
/// <remarks>
/// <para>
/// A SNAPSHOT, refreshed on a timer, and not a live read. A standings table asks the same question
/// about every player at once, and that is one <c>eth_call</c> each: doing it per request would make
/// a page anyone can open into a loop over the whole hotel, against a node shared with everything
/// else the chain does.
/// </para>
/// <para>
/// Held in memory rather than in a table on purpose. It is derived data — every figure in it can be
/// read back off the chain, which is the only authority for a balance — so persisting it would add a
/// schema, a migration and a second version of the truth to keep in step. A restart costs one refresh
/// cycle, not a number.
/// </para>
/// </remarks>
public interface IChainLeaderboard
{
    /// <summary>
    /// The standings, highest balance first. Empty before the first refresh, and empty for good on a
    /// hotel with no chain — which a caller should show as "no contest", not as an error.
    /// </summary>
    public ImmutableArray<ChainLeaderboardEntry> Standings { get; }

    /// <summary>
    /// When the standings were read, or <c>null</c> if they never have been. Shown to the player:
    /// a leaderboard that does not say how old it is invites the reader to assume it is live.
    /// </summary>
    public DateTime? TakenAtUtc { get; }
}

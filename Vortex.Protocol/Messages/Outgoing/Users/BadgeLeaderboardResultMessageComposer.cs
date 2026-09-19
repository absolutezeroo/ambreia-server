using Orleans;
using Vortex.Primitives.Badges.Snapshots;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Users;

/// <summary>One chunk of a badge leaderboard, in answer to <c>GetBadgeLeaderboard</c>.</summary>
/// <remarks>
/// Read by <c>unknowns/_SafePkg_1891/_SafeCls_3883.parse</c>: type, rarity, page, size,
/// totalEntries, then a counted block of rows, then a boolean and — if it is set — one more row for
/// the asking player.
/// <para>
/// The client drops the entire message when the size it reads back is not 50
/// (<c>BadgeLeaderboardDataServer.onBadgeLeaderboardResult</c>), so the chunk size travels through
/// unchanged from the request instead of being replaced by the number of rows that were found.
/// </para>
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record BadgeLeaderboardResultMessageComposer : IComposer
{
    [Id(0)]
    public required BadgeLeaderboardPageSnapshot Page { get; init; }
}

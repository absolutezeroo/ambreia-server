using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Users;

/// <summary>
/// A chunk of one badge leaderboard, asked for by the client's badge leaderboard window.
/// </summary>
/// <remarks>
/// Four ints, in the order <c>_SafePkg_1789/_SafeCls_3493</c> pushes them: board type, rarity,
/// chunk index, chunk size. Sent by
/// <c>groups/badge_leaderboard/server/BadgeLeaderboardDataServer.synchronizeChunk</c>, which always
/// asks for 50 and cuts the answer into five pages of ten.
/// </remarks>
public record GetBadgeLeaderboardMessage : IMessageEvent
{
    public required int Type { get; init; }

    /// <summary>-1 on the boards that do not use a tier; the client's own <c>DEFAULT_RARITY</c>.</summary>
    public required int Rarity { get; init; }

    public required int ChunkIndex { get; init; }

    public required int ChunkSize { get; init; }
}

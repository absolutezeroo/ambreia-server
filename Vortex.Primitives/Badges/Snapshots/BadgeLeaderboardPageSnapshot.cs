using System.Collections.Immutable;
using Orleans;

namespace Vortex.Primitives.Badges.Snapshots;

/// <summary>
/// One chunk of a badge leaderboard: the rows themselves, how many rows the whole board has, and
/// where the asking player sits if they are on it at all.
/// </summary>
/// <remarks>
/// The client asks in chunks of 50 and cuts each chunk into five pages of ten
/// (<c>groups/badge_leaderboard/server/BadgeLeaderboardDataServer</c>). It also refuses the whole
/// message unless <see cref="ChunkSize"/> comes back as exactly 50 — so the size is echoed from the
/// request rather than replaced with however many rows were found.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record BadgeLeaderboardPageSnapshot
{
    [Id(0)]
    public required BadgeLeaderboardType Type { get; init; }

    /// <summary>The rarity tier asked for, or -1 on the boards that do not use one.</summary>
    [Id(1)]
    public required int Rarity { get; init; }

    [Id(2)]
    public required int ChunkIndex { get; init; }

    [Id(3)]
    public required int ChunkSize { get; init; }

    [Id(4)]
    public required int TotalEntries { get; init; }

    [Id(5)]
    public required ImmutableArray<BadgeLeaderboardEntrySnapshot> Entries { get; init; }

    /// <summary>The asking player's own row, pinned below the list. Null when they are not ranked.</summary>
    [Id(6)]
    public BadgeLeaderboardEntrySnapshot? OwnEntry { get; init; }
}

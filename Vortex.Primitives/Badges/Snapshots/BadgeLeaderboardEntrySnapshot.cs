using Orleans;

namespace Vortex.Primitives.Badges.Snapshots;

/// <summary>One row of a badge leaderboard.</summary>
/// <remarks>
/// Five fields, in the order the client's row class reads them
/// (<c>unknowns/_SafePkg_1731/_SafeCls_3225</c>): userId, userName, figureString, rank, score.
/// <see cref="Rank"/> is 1-based, because the client prints it as it arrives.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record BadgeLeaderboardEntrySnapshot
{
    [Id(0)]
    public required int PlayerId { get; init; }

    [Id(1)]
    public required string Name { get; init; }

    [Id(2)]
    public required string Figure { get; init; }

    [Id(3)]
    public required int Rank { get; init; }

    [Id(4)]
    public required int Score { get; init; }
}

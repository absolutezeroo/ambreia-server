using Orleans;

namespace Vortex.Primitives.Badges.Snapshots;

/// <summary>
/// How many of a player's badges sit in one tier. The extended profile puts these on the wire as
/// a byte tier plus an int count, and the client answers <c>getBadgeCountByRarityId()</c> from them.
/// </summary>
/// <remarks>
/// Kept apart from the protocol's own <c>BadgeRarityCount</c>: this is what the directory grain
/// computes, that is what the composer carries, and the handler maps one to the other.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record BadgeRarityCountSnapshot
{
    [Id(0)]
    public required BadgeRarityTier Rarity { get; init; }

    [Id(1)]
    public required int Count { get; init; }
}

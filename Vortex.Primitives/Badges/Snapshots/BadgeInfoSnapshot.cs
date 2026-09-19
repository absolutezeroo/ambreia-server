using Orleans;

namespace Vortex.Primitives.Badges.Snapshots;

/// <summary>
/// What the hotel knows about one badge code: how many players hold it, and the tier that makes it.
/// </summary>
/// <remarks>
/// The client shows <see cref="OwnerCount"/> only while it is above zero and below 1000
/// (<c>groups/_SafeCls_2379.shouldShowOwnerCount</c>); at 1000 or more it prints "1000+" instead.
/// Nothing here is per-player, which is why it is not on <c>PlayerBadgeSnapshot</c>.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record BadgeInfoSnapshot
{
    [Id(0)]
    public required string BadgeCode { get; init; }

    [Id(1)]
    public required int OwnerCount { get; init; }

    [Id(2)]
    public required BadgeRarityTier Rarity { get; init; }
}

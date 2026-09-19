using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Inventory.Badges;

/// <summary>
/// The hotel's figures for one badge code, in answer to <c>GetBadgeInfo</c>.
/// </summary>
/// <remarks>
/// Layout from the client's parser, <c>unknowns/_SafePkg_3206/_SafeCls_4369.as</c>: int badgeId,
/// string badgeCode, int ownerCount, int badgeRarityId. The only consumer is
/// <c>FurnitureBadgeDisplayWidgetHandler.onBadgeInfo</c>, which uses <see cref="BadgeRarityId"/> to
/// pick the plaque's header colour, background theme and rarity line, and
/// <see cref="OwnerCount"/> for the "X owners" line — shown only while the count is above zero and
/// below 1000 (<c>groups/_SafeCls_2379.shouldShowOwnerCount</c>).
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record BadgeInfoMessageComposer : IComposer
{
    /// <summary>
    /// Parsed by the client and read by nothing: <c>_SafeCls_4369.badgeId</c> has no reader in this
    /// build. Sent as 0 rather than filled with a row id that would only look meaningful.
    /// </summary>
    [Id(0)]
    public int BadgeId { get; init; }

    [Id(1)]
    public required string BadgeCode { get; init; }

    [Id(2)]
    public required int OwnerCount { get; init; }

    [Id(3)]
    public required int BadgeRarityId { get; init; }
}

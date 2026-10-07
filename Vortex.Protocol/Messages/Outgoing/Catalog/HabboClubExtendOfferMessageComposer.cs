using Orleans;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Catalog;

[GenerateSerializer, Immutable]
public sealed record HabboClubExtendOfferMessageComposer : IComposer
{
    [Id(0)]
    public required MembershipOffer Offer { get; init; }

    [Id(1)]
    public int OriginalPricePerMonth { get; init; }

    [Id(2)]
    public int OriginalActivityPointPricePerMonth { get; init; }

    [Id(3)]
    public int OriginalActivityPointType { get; init; }

    [Id(4)]
    public int SubscriptionDaysLeft { get; init; }
}

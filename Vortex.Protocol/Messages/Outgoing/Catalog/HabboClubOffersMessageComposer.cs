using System.Collections.Generic;
using Orleans;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Catalog;

[GenerateSerializer, Immutable]
public sealed record HabboClubOffersMessageComposer : IComposer
{
    [Id(0)]
    public required IReadOnlyList<MembershipOffer> Offers { get; init; }

    [Id(1)]
    public required MembershipOfferRequestSourceType Source { get; init; }
}

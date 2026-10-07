using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Catalog;

public record GetClubOffersMessage : IMessageEvent
{
    public MembershipOfferRequestSourceType RequestSource { get; init; }
}

using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Catalog;
using Vortex.Revisions.Revision20260701.Serializers.Catalog.Data;

namespace Vortex.Revisions.Revision20260701.Serializers.Catalog;

internal class ClubGiftInfoEventMessageComposerSerializer(int header)
    : AbstractSerializer<ClubGiftInfoEventMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        ClubGiftInfoEventMessageComposer message
    )
    {
        packet
            .WriteInteger(message.DaysUntilNextGift)
            .WriteInteger(message.GiftsAvailable)
            .WriteInteger(message.Offers.Count);

        foreach (CatalogOfferSnapshot offer in message.Offers)
        {
            CatalogOfferSerializer.Serialize(packet, offer);
        }

        packet.WriteInteger(message.GiftData.Count);

        foreach (MembershipGiftOfferData gift in message.GiftData)
        {
            packet
                .WriteInteger(gift.OfferId)
                .WriteBoolean(gift.IsVip)
                .WriteInteger(gift.DaysRequired)
                .WriteBoolean(gift.IsSelectable);
        }
    }
}

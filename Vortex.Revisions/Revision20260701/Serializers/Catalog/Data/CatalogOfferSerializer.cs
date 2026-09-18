using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Packets;

namespace Vortex.Revisions.Revision20260701.Serializers.Catalog.Data;

internal class CatalogOfferSerializer
{
    public static void Serialize(IServerPacket packet, CatalogOfferSnapshot offer)
    {
        packet
            .WriteInteger(offer.Id)
            .WriteString(offer.LocalizationId)
            .WriteBoolean(offer.Rentable)
            .WriteInteger(offer.CostCredits)
            .WriteInteger(offer.CostCurrency)
            // Zero, not -1, when the offer names no currency: the client only reads this field
            // when the activity-point price is above zero, and then maps -1 and 0 to two different
            // currency icons (_SafeCls_1938.getIconStyleFor). The official server sends 0.
            .WriteInteger(offer.CurrencyTypeId ?? 0)
            .WriteInteger(offer.CostSilver)
            .WriteBoolean(offer.CanGift)
            .WriteInteger(offer.Products.Length);

        foreach (CatalogProductSnapshot product in offer.Products)
        {
            CatalogProductSerializer.Serialize(packet, product);
        }

        packet
            .WriteInteger(offer.ClubLevel)
            .WriteBoolean(offer.CanBundle)
            // Two fields we send constant because nothing here can fill them honestly yet.
            // The boolean is real and discriminating -- a 2026-09-18 capture of the official
            // server has it false on catalogue offers and true on every club-gift offer -- but it
            // is not exposed on the client's Offer class, so its meaning is still unknown and
            // guessing one would be worse than the constant. The preview image is a per-offer
            // string the official server fills (the Builders' Club page sends
            // "c_images/mobile_external_include/bc_1.png"); we have no column for it, and adding
            // one no admin page can edit would only move the gap.
            .WriteBoolean(false)
            .WriteString(string.Empty);
    }

    public static void SerializeAsPurchased(IServerPacket packet, CatalogOfferSnapshot offer)
    {
        packet
            .WriteInteger(offer.Id)
            .WriteString(offer.LocalizationId)
            .WriteBoolean(offer.Rentable)
            .WriteInteger(offer.CostCredits)
            .WriteInteger(offer.CostCurrency)
            .WriteInteger(offer.CurrencyTypeId ?? 0)
            .WriteBoolean(offer.CanGift)
            .WriteInteger(offer.Products.Length);

        foreach (CatalogProductSnapshot product in offer.Products)
        {
            CatalogProductSerializer.Serialize(packet, product);
        }

        packet.WriteInteger(offer.ClubLevel).WriteBoolean(offer.CanBundle);
    }
}

using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Marketplace;

namespace Vortex.Revisions.Revision20260701.Serializers.Marketplace;

internal class MarketplaceConfigurationEventMessageComposerSerializer(int header)
    : AbstractSerializer<MarketplaceConfigurationEventMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        MarketplaceConfigurationEventMessageComposer message
    )
    {
        packet.WriteBoolean(message.Enabled);
        packet.WriteInteger(message.Commission);
        packet.WriteInteger(message.TokenBatchPrice);
        packet.WriteInteger(message.TokenBatchSize);
        packet.WriteInteger(message.OfferMinPrice);
        packet.WriteInteger(message.OfferMaxPrice);
        packet.WriteInteger(message.ExpirationHours);
        packet.WriteInteger(message.AveragePricePeriod);
        packet.WriteInteger(message.SellingFeePercentage);
        packet.WriteInteger(message.RevenueLimit);
        packet.WriteInteger(message.HalfTaxLimit);
    }
}

using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Marketplace;
using Vortex.Revisions.Configuration;
using Vortex.Revisions.Revision20260701.Parsers.Marketplace;
using Vortex.Revisions.Revision20260701.Serializers.Marketplace;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class MarketplaceMap(ProtocolLimitsConfig protocolLimits) : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(
            MessageEvent.BuyMarketplaceOfferMessageEvent,
            new BuyMarketplaceOfferMessageParser()
        );
        builder.MapParser(
            MessageEvent.CancelMarketplaceOfferMessageEvent,
            new CancelMarketplaceOfferMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetMarketplaceCanMakeOfferMessageEvent,
            new GetMarketplaceCanMakeOfferMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetMarketplaceConfigurationMessageEvent,
            new GetMarketplaceConfigurationMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetMarketplaceItemStatsEvent,
            new GetMarketplaceItemStatsMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetMarketplaceOffersMessageEvent,
            new GetMarketplaceOffersMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetMarketplaceOwnOffersMessageEvent,
            new GetMarketplaceOwnOffersMessageParser()
        );
        builder.MapParser(
            MessageEvent.MakeOfferMessageEvent,
            new MakeOfferMessageParser(protocolLimits.MaxMarketplaceOfferItems)
        );
        builder.MapParser(
            MessageEvent.RedeemMarketplaceOfferCreditsMessageEvent,
            new RedeemMarketplaceOfferCreditsMessageParser()
        );

        builder.MapSerializer(
            typeof(MarketplaceBuyOfferResultEventMessageComposer),
            new MarketplaceBuyOfferResultEventMessageComposerSerializer(
                MessageComposer.MarketplaceBuyOfferResultComposer
            )
        );
        builder.MapSerializer(
            typeof(MarketplaceCancelOfferResultEventMessageComposer),
            new MarketplaceCancelOfferResultEventMessageComposerSerializer(
                MessageComposer.MarketplaceCancelOfferResultComposer
            )
        );
        builder.MapSerializer(
            typeof(MarketplaceCanMakeOfferResultMessageComposer),
            new MarketplaceCanMakeOfferResultMessageComposerSerializer(
                MessageComposer.MarketplaceCanMakeOfferResult
            )
        );
        builder.MapSerializer(
            typeof(MarketplaceConfigurationEventMessageComposer),
            new MarketplaceConfigurationEventMessageComposerSerializer(
                MessageComposer.MarketplaceConfigurationComposer
            )
        );
        builder.MapSerializer(
            typeof(MarketplaceItemStatsEventMessageComposer),
            new MarketplaceItemStatsEventMessageComposerSerializer(
                MessageComposer.MarketplaceItemStatsComposer
            )
        );
        builder.MapSerializer(
            typeof(MarketplaceMakeOfferResultMessageComposer),
            new MarketplaceMakeOfferResultMessageComposerSerializer(
                MessageComposer.MarketplaceMakeOfferResult
            )
        );
        builder.MapSerializer(
            typeof(MarketPlaceOffersEventMessageComposer),
            new MarketPlaceOffersEventMessageComposerSerializer(
                MessageComposer.MarketPlaceOffersComposer
            )
        );
        builder.MapSerializer(
            typeof(MarketPlaceOwnOffersEventMessageComposer),
            new MarketPlaceOwnOffersEventMessageComposerSerializer(
                MessageComposer.MarketPlaceOwnOffersComposer
            )
        );
    }
}

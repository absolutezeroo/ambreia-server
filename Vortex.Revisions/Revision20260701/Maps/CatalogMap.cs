using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Catalog;
using Vortex.Revisions.Revision20260701.Parsers.Catalog;
using Vortex.Revisions.Revision20260701.Serializers.Catalog;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class CatalogMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        // charge firework?
        builder.MapParser(
            MessageEvent.GetBundleDiscountRulesetEvent,
            new GetBundleDiscountRulesetMessageParser()
        );
        builder.MapParser(MessageEvent.GetCatalogIndexEvent, new GetCatalogIndexMessageParser());
        builder.MapParser(MessageEvent.GetCatalogPageEvent, new GetCatalogPageMessageParser());
        builder.MapParser(MessageEvent.GetClubGiftMessageEvent, new GetClubGiftInfoMessageParser());
        builder.MapParser(MessageEvent.GetClubOffersMessageEvent, new GetClubOffersMessageParser());
        builder.MapParser(
            MessageEvent.GetGiftWrappingConfigurationEvent,
            new GetGiftWrappingConfigurationMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetHabboClubExtendOfferMessageEvent,
            new GetHabboClubExtendOfferMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetIsOfferGiftableEvent,
            new GetIsOfferGiftableMessageParser()
        );
        builder.MapParser(MessageEvent.GetProductOfferEvent, new GetProductOfferMessageParser());
        builder.MapParser(
            MessageEvent.GetSellablePetPalettesEvent,
            new GetSellablePetPalettesMessageParser()
        );
        builder.MapParser(
            MessageEvent.MarkCatalogNewAdditionsPageOpenedEvent,
            new MarkCatalogNewAdditionsPageOpenedMessageParser()
        );
        builder.MapParser(
            MessageEvent.PurchaseBasicMembershipExtensionEvent,
            new PurchaseBasicMembershipExtensionMessageParser()
        );
        builder.MapParser(
            MessageEvent.PurchaseFromCatalogAsGiftEvent,
            new PurchaseFromCatalogAsGiftMessageParser()
        );
        builder.MapParser(
            MessageEvent.PurchaseFromCatalogEvent,
            new PurchaseFromCatalogMessageParser()
        );
        builder.MapParser(
            MessageEvent.PurchaseVipMembershipExtensionEvent,
            new PurchaseVipMembershipExtensionMessageParser()
        );
        builder.MapParser(MessageEvent.SelectClubGiftEvent, new SelectClubGiftMessageParser());

        builder.MapSerializer(
            typeof(BundleDiscountRulesetMessageComposer),
            new BundleDiscountRulesetMessageComposerSerializer(
                MessageComposer.BundleDiscountRulesetMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(CatalogIndexMessageComposer),
            new CatalogIndexMessageComposerSerializer(MessageComposer.CatalogIndexMessageComposer)
        );
        builder.MapSerializer(
            typeof(CatalogPageMessageComposer),
            new CatalogPageMessageComposerSerializer(MessageComposer.CatalogPageMessageComposer)
        );
        builder.MapSerializer(
            typeof(ClubGiftInfoEventMessageComposer),
            new ClubGiftInfoEventMessageComposerSerializer(MessageComposer.ClubGiftInfoComposer)
        );
        builder.MapSerializer(
            typeof(ClubGiftSelectedEventMessageComposer),
            new ClubGiftSelectedEventMessageComposerSerializer(
                MessageComposer.ClubGiftSelectedComposer
            )
        );
        builder.MapSerializer(
            typeof(GiftReceiverNotFoundEventMessageComposer),
            new GiftReceiverNotFoundEventMessageComposerSerializer(
                MessageComposer.GiftReceiverNotFoundComposer
            )
        );
        builder.MapSerializer(
            typeof(GiftWrappingConfigurationEventMessageComposer),
            new GiftWrappingConfigurationEventMessageComposerSerializer(
                MessageComposer.GiftWrappingConfigurationComposer
            )
        );
        builder.MapSerializer(
            typeof(HabboClubExtendOfferMessageComposer),
            new HabboClubExtendOfferMessageComposerSerializer(
                MessageComposer.HabboClubExtendOfferMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(HabboClubOffersMessageComposer),
            new HabboClubOffersMessageComposerSerializer(
                MessageComposer.HabboClubOffersMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(IsOfferGiftableEventMessageComposer),
            new IsOfferGiftableEventMessageComposerSerializer(
                MessageComposer.IsOfferGiftableEventMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(NotEnoughBalanceMessageComposer),
            new NotEnoughBalanceMessageComposerSerializer(
                MessageComposer.NotEnoughBalanceMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ProductOfferEventMessageComposer),
            new ProductOfferEventMessageComposerSerializer(MessageComposer.ProductOfferComposer)
        );
        builder.MapSerializer(
            typeof(PurchaseErrorMessageComposer),
            new PurchaseErrorMessageComposerSerializer(MessageComposer.PurchaseErrorMessageComposer)
        );
        builder.MapSerializer(
            typeof(PurchaseNotAllowedMessageComposer),
            new PurchaseNotAllowedMessageComposerSerializer(
                MessageComposer.PurchaseNotAllowedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(PurchaseOKMessageComposer),
            new PurchaseOKMessageComposerSerializer(MessageComposer.PurchaseOKMessageComposer)
        );
        builder.MapSerializer(
            typeof(SellablePetPalettesMessageComposer),
            new SellablePetPalettesMessageComposerSerializer(
                MessageComposer.SellablePetPalettesMessageComposer
            )
        );
    }
}

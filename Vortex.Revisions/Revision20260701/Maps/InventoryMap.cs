using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Inventory.Achievements;
using Vortex.Protocol.Messages.Outgoing.Inventory.Avatareffect;
using Vortex.Protocol.Messages.Outgoing.Inventory.Badges;
using Vortex.Protocol.Messages.Outgoing.Inventory.Bots;
using Vortex.Protocol.Messages.Outgoing.Inventory.Clothing;
using Vortex.Protocol.Messages.Outgoing.Inventory.Furni;
using Vortex.Protocol.Messages.Outgoing.Inventory.Pets;
using Vortex.Protocol.Messages.Outgoing.Inventory.Purse;
using Vortex.Protocol.Messages.Outgoing.Inventory.Trading;
using Vortex.Protocol.Messages.Outgoing.Users;
using Vortex.Revisions.Configuration;
using Vortex.Revisions.Revision20260701.Parsers.Inventory.Achievements;
using Vortex.Revisions.Revision20260701.Parsers.Inventory.Avatareffect;
using Vortex.Revisions.Revision20260701.Parsers.Inventory.Badges;
using Vortex.Revisions.Revision20260701.Parsers.Inventory.Clothing;
using Vortex.Revisions.Revision20260701.Parsers.Inventory.Furni;
using Vortex.Revisions.Revision20260701.Parsers.Inventory.Pets;
using Vortex.Revisions.Revision20260701.Parsers.Inventory.Purse;
using Vortex.Revisions.Revision20260701.Parsers.Inventory.Trading;
using Vortex.Revisions.Revision20260701.Serializers.Inventory.Achievements;
using Vortex.Revisions.Revision20260701.Serializers.Inventory.Avatareffect;
using Vortex.Revisions.Revision20260701.Serializers.Inventory.Badges;
using Vortex.Revisions.Revision20260701.Serializers.Inventory.Bots;
using Vortex.Revisions.Revision20260701.Serializers.Inventory.Clothing;
using Vortex.Revisions.Revision20260701.Serializers.Inventory.Furni;
using Vortex.Revisions.Revision20260701.Serializers.Inventory.Pets;
using Vortex.Revisions.Revision20260701.Serializers.Inventory.Purse;
using Vortex.Revisions.Revision20260701.Serializers.Inventory.Trading;
using Vortex.Revisions.Revision20260701.Serializers.Users;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class InventoryMap : IRevisionMap
{
    private readonly ProtocolLimitsConfig _protocolLimits;

    public InventoryMap(ProtocolLimitsConfig protocolLimits)
    {
        _protocolLimits = protocolLimits;
    }

    public void RegisterInto(IRevisionMapBuilder builder)
    {
        // Inventory Achievements
        builder.MapParser(MessageEvent.GetAchievementsEvent, new GetAchievementsMessageParser());

        // Inventory Avatar Effects
        builder.MapParser(
            MessageEvent.AvatarEffectActivatedEvent,
            new AvatarEffectActivatedMessageParser()
        );
        builder.MapParser(
            MessageEvent.AvatarEffectSelectedEvent,
            new AvatarEffectSelectedMessageParser()
        );

        // Inventory Badges
        builder.MapParser(
            MessageEvent.GetBadgePointLimitsEvent,
            new GetBadgePointLimitsMessageParser()
        );
        builder.MapParser(MessageEvent.GetBadgesEvent, new GetBadgesMessageParser());
        builder.MapParser(MessageEvent.GetBadgeInfoEvent, new GetBadgeInfoMessageParser());
        builder.MapParser(
            MessageEvent.GetIsBadgeRequestFulfilledEvent,
            new GetIsBadgeRequestFulfilledMessageParser()
        );
        builder.MapParser(MessageEvent.RequestABadgeEvent, new RequestABadgeMessageParser());
        builder.MapParser(
            MessageEvent.SetActivatedBadgesEvent,
            new SetActivatedBadgesMessageParser()
        );

        // Inventory Bots

        // Inventory Furni
        builder.MapParser(
            MessageEvent.RequestFurniInventoryEvent,
            new RequestFurniInventoryMessageParser()
        );
        builder.MapParser(
            MessageEvent.RequestFurniInventoryWhenNotInRoomEvent,
            new RequestFurniInventoryWhenNotInRoomMessageParser()
        );
        builder.MapParser(
            MessageEvent.RequestRoomPropertySet,
            new RequestRoomPropertySetMessageParser()
        );

        // Inventory Pets
        builder.MapParser(MessageEvent.GetPetInventoryEvent, new GetPetInventoryMessageParser());

        // Inventory Purse
        builder.MapParser(MessageEvent.GetCreditsInfoEvent, new GetCreditsInfoMessageParser());

        // Inventory Clothing
        builder.MapParser(
            MessageEvent.RedeemPurchasableClothingMessageEvent,
            new RedeemPurchasableClothingMessageParser()
        );

        // Inventory Trading
        builder.MapParser(MessageEvent.AcceptTradingEvent, new AcceptTradingMessageParser());
        builder.MapParser(
            MessageEvent.AddItemsToTradeEvent,
            new AddItemsToTradeMessageParser(_protocolLimits.MaxTradeItems)
        );
        builder.MapParser(MessageEvent.AddItemToTradeEvent, new AddItemToTradeMessageParser());
        builder.MapParser(MessageEvent.CloseTradingEvent, new CloseTradingMessageParser());
        builder.MapParser(
            MessageEvent.ConfirmAcceptTradingEvent,
            new ConfirmAcceptTradingMessageParser()
        );
        builder.MapParser(
            MessageEvent.ConfirmDeclineTradingEvent,
            new ConfirmDeclineTradingMessageParser()
        );
        builder.MapParser(MessageEvent.OpenTradingEvent, new OpenTradingMessageParser());
        builder.MapParser(
            MessageEvent.RemoveItemFromTradeEvent,
            new RemoveItemFromTradeMessageParser()
        );
        builder.MapParser(MessageEvent.SilverFeeMessageEvent, new SilverFeeMessageParser());
        builder.MapParser(MessageEvent.UnacceptTradingEvent, new UnacceptTradingMessageParser());

        // Inventory Achievements
        builder.MapSerializer(
            typeof(AchievementEventMessageComposer),
            new AchievementEventMessageComposerSerializer(MessageComposer.AchievementComposer)
        );
        builder.MapSerializer(
            typeof(AchievementsEventMessageComposer),
            new AchievementsEventMessageComposerSerializer(MessageComposer.AchievementsComposer)
        );
        builder.MapSerializer(
            typeof(AchievementsScoreEventMessageComposer),
            new AchievementsScoreEventMessageComposerSerializer(
                MessageComposer.AchievementsScoreComposer
            )
        );

        // Inventory Avatareffect
        builder.MapSerializer(
            typeof(AvatarEffectActivatedMessageComposer),
            new AvatarEffectActivatedMessageComposerSerializer(
                MessageComposer.AvatarEffectActivatedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(AvatarEffectAddedMessageComposer),
            new AvatarEffectAddedMessageComposerSerializer(
                MessageComposer.AvatarEffectAddedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(AvatarEffectExpiredMessageComposer),
            new AvatarEffectExpiredMessageComposerSerializer(
                MessageComposer.AvatarEffectExpiredMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(AvatarEffectSelectedMessageComposer),
            new AvatarEffectSelectedMessageComposerSerializer(
                MessageComposer.AvatarEffectSelectedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(AvatarEffectsMessageComposer),
            new AvatarEffectsMessageComposerSerializer(MessageComposer.AvatarEffectsMessageComposer)
        );

        // Inventory Badges
        builder.MapSerializer(
            typeof(BadgePointLimitsEventMessageComposer),
            new BadgePointLimitsEventMessageComposerSerializer(
                MessageComposer.BadgePointLimitsComposer
            )
        );
        builder.MapSerializer(
            typeof(BadgeReceivedEventMessageComposer),
            new BadgeReceivedEventMessageComposerSerializer(MessageComposer.BadgeReceivedComposer)
        );
        builder.MapSerializer(
            typeof(BadgeInfoMessageComposer),
            new BadgeInfoMessageComposerSerializer(MessageComposer.BadgeInfoComposer)
        );
        builder.MapSerializer(
            typeof(BadgesEventMessageComposer),
            new BadgesEventMessageComposerSerializer(MessageComposer.BadgesComposer)
        );
        builder.MapSerializer(
            typeof(HabboUserBadgesMessageComposer),
            new HabboUserBadgesMessageComposerSerializer(
                MessageComposer.HabboUserBadgesMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(IsBadgeRequestFulfilledEventMessageComposer),
            new IsBadgeRequestFulfilledEventMessageComposerSerializer(
                MessageComposer.IsBadgeRequestFulfilledComposer
            )
        );

        // Inventory Bots
        builder.MapSerializer(
            typeof(BotAddedToInventoryEventMessageComposer),
            new BotAddedToInventoryEventMessageComposerSerializer(
                MessageComposer.BotAddedToInventoryComposer
            )
        );
        builder.MapSerializer(
            typeof(BotRemovedFromInventoryEventMessageComposer),
            new BotRemovedFromInventoryEventMessageComposerSerializer(
                MessageComposer.BotRemovedFromInventoryComposer
            )
        );

        // Inventory Clothing
        builder.MapSerializer(
            typeof(FigureSetIdsEventMessageComposer),
            new FigureSetIdsEventMessageComposerSerializer(MessageComposer.FigureSetIdsComposer)
        );

        // Inventory Furni
        builder.MapSerializer(
            typeof(FurniListAddOrUpdateEventMessageComposer),
            new FurniListAddOrUpdateEventMessageComposerSerializer(
                MessageComposer.FurniListAddOrUpdateComposer
            )
        );
        builder.MapSerializer(
            typeof(FurniListEventMessageComposer),
            new FurniListEventMessageComposerSerializer(MessageComposer.FurniListComposer)
        );
        builder.MapSerializer(
            typeof(FurniListInvalidateEventMessageComposer),
            new FurniListInvalidateEventMessageComposerSerializer(
                MessageComposer.FurniListInvalidateComposer
            )
        );
        builder.MapSerializer(
            typeof(FurniListRemoveEventMessageComposer),
            new FurniListRemoveEventMessageComposerSerializer(
                MessageComposer.FurniListRemoveComposer
            )
        );

        // Inventory Pets
        builder.MapSerializer(
            typeof(PetAddedToInventoryEventMessageComposer),
            new PetAddedToInventoryEventMessageComposerSerializer(
                MessageComposer.PetAddedToInventoryComposer
            )
        );
        builder.MapSerializer(
            typeof(PetInventoryEventMessageComposer),
            new PetInventoryEventMessageComposerSerializer(MessageComposer.PetInventoryComposer)
        );
        builder.MapSerializer(
            typeof(PetRemovedFromInventoryEventMessageComposer),
            new PetRemovedFromInventoryEventMessageComposerSerializer(
                MessageComposer.PetRemovedFromInventoryComposer
            )
        );

        // Inventory Purse
        builder.MapSerializer(
            typeof(CreditBalanceEventMessageComposer),
            new CreditBalanceEventMessageComposerSerializer(MessageComposer.CreditBalanceComposer)
        );

        // Inventory Trading
        builder.MapSerializer(
            typeof(TradeOpenFailedEventPaserMessageComposer),
            new TradeOpenFailedEventPaserMessageComposerSerializer(
                MessageComposer.TradeOpenFailedComposer
            )
        );
        builder.MapSerializer(
            typeof(TradingAcceptEventMessageComposer),
            new TradingAcceptEventMessageComposerSerializer(MessageComposer.TradingAcceptComposer)
        );
        builder.MapSerializer(
            typeof(TradingCloseEventMessageComposer),
            new TradingCloseEventMessageComposerSerializer(MessageComposer.TradingCloseComposer)
        );
        builder.MapSerializer(
            typeof(TradingCompletedEventMessageComposer),
            new TradingCompletedEventMessageComposerSerializer(
                MessageComposer.TradingCompletedComposer
            )
        );
        builder.MapSerializer(
            typeof(TradingConfirmationEventMessageComposer),
            new TradingConfirmationEventMessageComposerSerializer(
                MessageComposer.TradingConfirmationComposer
            )
        );
        builder.MapSerializer(
            typeof(TradingItemListEventMessageComposer),
            new TradingItemListEventMessageComposerSerializer(
                MessageComposer.TradingItemListComposer
            )
        );
        builder.MapSerializer(
            typeof(TradingNotOpenEventMessageComposer),
            new TradingNotOpenEventMessageComposerSerializer(MessageComposer.TradingNotOpenComposer)
        );
        builder.MapSerializer(
            typeof(TradingOpenEventMessageComposer),
            new TradingOpenEventMessageComposerSerializer(MessageComposer.TradingOpenComposer)
        );
    }
}

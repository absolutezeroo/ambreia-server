using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Room.Action;
using Vortex.Protocol.Messages.Outgoing.Room.Bots;
using Vortex.Protocol.Messages.Outgoing.Room.Chat;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Room.Furniture;
using Vortex.Protocol.Messages.Outgoing.Room.Layout;
using Vortex.Protocol.Messages.Outgoing.Room.Permissions;
using Vortex.Protocol.Messages.Outgoing.Room.Pets;
using Vortex.Protocol.Messages.Outgoing.Room.Session;
using Vortex.Revisions.Revision20260701.Parsers.Room.Action;
using Vortex.Revisions.Revision20260701.Parsers.Room.Avatar;
using Vortex.Revisions.Revision20260701.Parsers.Room.Bots;
using Vortex.Revisions.Revision20260701.Parsers.Room.Chat;
using Vortex.Revisions.Revision20260701.Parsers.Room.Engine;
using Vortex.Revisions.Revision20260701.Parsers.Room.Furniture;
using Vortex.Revisions.Revision20260701.Parsers.Room.Layout;
using Vortex.Revisions.Revision20260701.Parsers.Room.Pets;
using Vortex.Revisions.Revision20260701.Parsers.Room.Session;
using Vortex.Revisions.Revision20260701.Serializers.Room.Action;
using Vortex.Revisions.Revision20260701.Serializers.Room.Bots;
using Vortex.Revisions.Revision20260701.Serializers.Room.Chat;
using Vortex.Revisions.Revision20260701.Serializers.Room.Engine;
using Vortex.Revisions.Revision20260701.Serializers.Room.Furniture;
using Vortex.Revisions.Revision20260701.Serializers.Room.Layout;
using Vortex.Revisions.Revision20260701.Serializers.Room.Permissions;
using Vortex.Revisions.Revision20260701.Serializers.Room.Pets;
using Vortex.Revisions.Revision20260701.Serializers.Room.Session;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class RoomMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        // Room Action
        builder.MapParser(
            MessageEvent.AmbassadorAlertMessageEvent,
            new AmbassadorAlertMessageParser()
        );
        builder.MapParser(MessageEvent.AssignRightsMessageEvent, new AssignRightsMessageParser());
        builder.MapParser(
            MessageEvent.BanUserWithDurationMessageEvent,
            new BanUserWithDurationMessageParser()
        );
        builder.MapParser(MessageEvent.KickUserMessageEvent, new KickUserMessageParser());
        builder.MapParser(MessageEvent.LetUserInMessageEvent, new LetUserInMessageParser());
        builder.MapParser(MessageEvent.MuteAllInRoomEvent, new MuteAllInRoomMessageParser());
        builder.MapParser(MessageEvent.MuteUserMessageEvent, new MuteUserMessageParser());
        builder.MapParser(
            MessageEvent.RemoveAllRightsMessageEvent,
            new RemoveAllRightsMessageParser()
        );
        builder.MapParser(MessageEvent.RemoveRightsMessageEvent, new RemoveRightsMessageParser());
        builder.MapParser(
            MessageEvent.UnbanUserFromRoomMessageEvent,
            new UnbanUserFromRoomMessageParser()
        );
        builder.MapParser(MessageEvent.UnmuteUserMessageEvent, new UnmuteUserMessageParser());

        // Room Avatar
        builder.MapParser(
            MessageEvent.AvatarExpressionMessageEvent,
            new AvatarExpressionMessageParser()
        );
        builder.MapParser(MessageEvent.ChangeMottoMessageEvent, new ChangeMottoMessageParser());
        builder.MapParser(MessageEvent.ChangePostureMessageEvent, new ChangePostureMessageParser());
        builder.MapParser(MessageEvent.DanceMessageEvent, new DanceMessageParser());
        builder.MapParser(MessageEvent.DropCarryItemMessageEvent, new DropCarryItemMessageParser());
        builder.MapParser(MessageEvent.LookToMessageEvent, new LookToMessageParser());
        builder.MapParser(MessageEvent.PassCarryItemMessageEvent, new PassCarryItemMessageParser());
        builder.MapParser(
            MessageEvent.PassCarryItemToPetMessageEvent,
            new PassCarryItemToPetMessageParser()
        );
        builder.MapParser(MessageEvent.SignMessageEvent, new SignMessageParser());

        // Room Bots
        builder.MapParser(MessageEvent.CommandBotEvent, new CommandBotMessageParser());
        builder.MapParser(
            MessageEvent.GetBotCommandConfigurationDataEvent,
            new GetBotCommandConfigurationDataMessageParser()
        );

        // Room Chat
        builder.MapParser(MessageEvent.CancelTypingMessageEvent, new CancelTypingMessageParser());
        builder.MapParser(MessageEvent.ChatMessageEvent, new ChatMessageParser());
        builder.MapParser(MessageEvent.ShoutMessageEvent, new ShoutMessageParser());
        builder.MapParser(MessageEvent.StartTypingMessageEvent, new StartTypingMessageParser());
        builder.MapParser(MessageEvent.WhisperMessageEvent, new WhisperMessageParser());

        // Room Engine
        builder.MapParser(MessageEvent.ClickCharacterEvent, new ClickCharacterMessageParser());
        builder.MapParser(MessageEvent.ClickFurniMessageEvent, new ClickFurniMessageParser());
        builder.MapParser(
            MessageEvent.GetFurnitureAliasesMessageEvent,
            new GetFurnitureAliasesMessageParser()
        );
        builder.MapParser(MessageEvent.GetItemDataMessageEvent, new GetItemDataMessageParser());
        builder.MapParser(
            MessageEvent.GetPetCommandsMessageEvent,
            new GetPetCommandsMessageParser()
        );
        builder.MapParser(MessageEvent.MountPetMessageEvent, new MountPetMessageParser());
        builder.MapParser(MessageEvent.MoveAvatarMessageEvent, new MoveAvatarMessageParser());
        builder.MapParser(MessageEvent.MoveObjectMessageEvent, new MoveObjectMessageParser());
        builder.MapParser(MessageEvent.MovePetMessageEvent, new MovePetMessageParser());
        builder.MapParser(MessageEvent.MoveWallItemMessageEvent, new MoveWallItemMessageParser());
        builder.MapParser(
            MessageEvent.VortexGetFurniEditorDataMessageEvent,
            new VortexGetFurniEditorDataMessageParser()
        );
        builder.MapParser(
            MessageEvent.VortexApplyFurniEditMessageEvent,
            new VortexApplyFurniEditMessageParser()
        );
        builder.MapParser(
            MessageEvent.VortexGetFurniDefinitionMessageEvent,
            new VortexGetFurniDefinitionMessageParser()
        );
        builder.MapParser(
            MessageEvent.VortexApplyFurniDefinitionMessageEvent,
            new VortexApplyFurniDefinitionMessageParser()
        );
        builder.MapParser(MessageEvent.PickupObjectMessageEvent, new PickupObjectMessageParser());
        builder.MapParser(MessageEvent.PlaceBotMessageEvent, new PlaceBotMessageParser());
        builder.MapParser(MessageEvent.PlaceObjectMessageEvent, new PlaceObjectMessageParser());
        builder.MapParser(MessageEvent.PlacePetMessageEvent, new PlacePetMessageParser());
        builder.MapParser(
            MessageEvent.RemoveBotFromFlatMessageEvent,
            new RemoveBotFromFlatMessageParser()
        );
        builder.MapParser(MessageEvent.RemoveItemMessageEvent, new RemoveItemMessageParser());
        builder.MapParser(
            MessageEvent.RemovePetFromFlatMessageEvent,
            new RemovePetFromFlatMessageParser()
        );
        builder.MapParser(
            MessageEvent.RemoveSaddleFromPetMessageEvent,
            new RemoveSaddleFromPetMessageParser()
        );
        builder.MapParser(
            MessageEvent.SetClothingChangeDataMessageEvent,
            new SetClothingChangeDataMessageParser()
        );
        builder.MapParser(MessageEvent.SetItemDataMessageEvent, new SetItemDataMessageParser());
        builder.MapParser(MessageEvent.SetObjectDataMessageEvent, new SetObjectDataMessageParser());
        builder.MapParser(
            MessageEvent.TogglePetRidingPermissionMessageEvent,
            new TogglePetRidingPermissionMessageParser()
        );
        builder.MapParser(MessageEvent.UseFurnitureMessageEvent, new UseFurnitureMessageParser());
        builder.MapParser(MessageEvent.UseWallItemMessageEvent, new UseWallItemMessageParser());

        // Room Furniture
        builder.MapParser(
            MessageEvent.AddSpamWallPostItMessageEvent,
            new AddSpamWallPostItMessageParser()
        );
        builder.MapParser(
            MessageEvent.ControlYoutubeDisplayPlaybackMessageEvent,
            new ControlYoutubeDisplayPlaybackMessageParser()
        );
        builder.MapParser(
            MessageEvent.CreditFurniRedeemMessageEvent,
            new CreditFurniRedeemMessageParser()
        );
        builder.MapParser(MessageEvent.DiceOffMessageEvent, new DiceOffMessageParser());
        builder.MapParser(
            MessageEvent.EnterOneWayDoorMessageEvent,
            new EnterOneWayDoorMessageParser()
        );
        builder.MapParser(
            MessageEvent.ExtendRentOrBuyoutFurniMessageEvent,
            new ExtendRentOrBuyoutFurniMessageParser()
        );
        builder.MapParser(
            MessageEvent.ExtendRentOrBuyoutStripItemMessageEvent,
            new ExtendRentOrBuyoutStripItemMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetGuildFurniContextMenuInfoMessageEvent,
            new GetGuildFurniContextMenuInfoMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetRentOrBuyoutOfferMessageEvent,
            new GetRentOrBuyoutOfferMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetYoutubeDisplayStatusMessageEvent,
            new GetYoutubeDisplayStatusMessageParser()
        );
        builder.MapParser(
            MessageEvent.OpenPetPackageMessageEvent,
            new OpenPetPackageMessageParser()
        );
        builder.MapParser(MessageEvent.PlacePostItMessageEvent, new PlacePostItMessageParser());
        builder.MapParser(MessageEvent.PresentOpenMessageEvent, new PresentOpenMessageParser());
        builder.MapParser(
            MessageEvent.RentableSpaceCancelRentMessageEvent,
            new RentableSpaceCancelRentMessageParser()
        );
        builder.MapParser(
            MessageEvent.RentableSpaceConfigureMessageEvent,
            new ConfigureRentableSpaceMessageParser()
        );
        builder.MapParser(
            MessageEvent.RentableSpaceGetConfigMessageEvent,
            new GetRentableSpaceConfigMessageParser()
        );
        builder.MapParser(
            MessageEvent.RentableSpaceRentMessageEvent,
            new RentableSpaceRentMessageParser()
        );
        builder.MapParser(
            MessageEvent.RentableSpaceStatusMessageEvent,
            new RentableSpaceStatusMessageParser()
        );
        builder.MapParser(
            MessageEvent.RoomDimmerChangeStateMessageEvent,
            new RoomDimmerChangeStateMessageParser()
        );
        builder.MapParser(
            MessageEvent.RoomDimmerGetPresetsMessageEvent,
            new RoomDimmerGetPresetsMessageParser()
        );
        builder.MapParser(
            MessageEvent.RoomDimmerSavePresetMessageEvent,
            new RoomDimmerSavePresetMessageParser()
        );
        builder.MapParser(MessageEvent.SetAreaHideDataEvent, new SetAreaHideDataMessageParser());
        builder.MapParser(
            MessageEvent.SetAdjacentCustomStackingHeightEvent,
            new SetAdjacentCustomStackingHeightMessageParser()
        );
        builder.MapParser(
            MessageEvent.SetCustomStackingHeightEvent,
            new SetCustomStackingHeightMessageParser()
        );
        builder.MapParser(
            MessageEvent.SetMannequinFigureEvent,
            new SetMannequinFigureMessageParser()
        );
        builder.MapParser(MessageEvent.SetMannequinNameEvent, new SetMannequinNameMessageParser());
        builder.MapParser(
            MessageEvent.SetRandomStateMessageEvent,
            new SetRandomStateMessageParser()
        );
        builder.MapParser(
            MessageEvent.SetRoomBackgroundColorDataEvent,
            new SetRoomBackgroundColorDataMessageParser()
        );
        builder.MapParser(
            MessageEvent.SetYoutubeDisplayPlaylistMessageEvent,
            new SetYoutubeDisplayPlaylistMessageParser()
        );
        builder.MapParser(
            MessageEvent.SpinWheelOfFortuneMessageEvent,
            new SpinWheelOfFortuneMessageParser()
        );
        builder.MapParser(MessageEvent.ThrowDiceMessageEvent, new ThrowDiceMessageParser());

        // Room Layout
        builder.MapParser(
            MessageEvent.GetOccupiedTilesMessageEvent,
            new GetOccupiedTilesMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetRoomEntryTileMessageEvent,
            new GetRoomEntryTileMessageParser()
        );
        builder.MapParser(
            MessageEvent.UpdateFloorPropertiesMessageEvent,
            new UpdateFloorPropertiesMessageParser()
        );

        // Room Pets
        builder.MapParser(
            MessageEvent.CustomizePetWithFurniEvent,
            new CustomizePetWithFurniMessageParser()
        );
        builder.MapParser(MessageEvent.GetPetInfoMessageEvent, new GetPetInfoMessageParser());
        builder.MapParser(MessageEvent.PetSelectedMessageEvent, new PetSelectedMessageParser());
        builder.MapParser(MessageEvent.RespectPetMessageEvent, new RespectPetMessageParser());

        // Room Session
        builder.MapParser(MessageEvent.ChangeQueueMessageEvent, new ChangeQueueMessageParser());
        builder.MapParser(
            MessageEvent.OpenFlatConnectionMessageEvent,
            new OpenFlatConnectionMessageParser()
        );
        builder.MapParser(MessageEvent.QuitMessageEvent, new QuitMessageParser());

        // Room Action
        builder.MapSerializer(
            typeof(AvatarEffectMessageComposer),
            new AvatarEffectMessageComposerSerializer(MessageComposer.AvatarEffectMessageComposer)
        );
        builder.MapSerializer(
            typeof(CarryObjectMessageComposer),
            new CarryObjectMessageComposerSerializer(MessageComposer.CarryObjectMessageComposer)
        );
        builder.MapSerializer(
            typeof(DanceMessageComposer),
            new DanceMessageComposerSerializer(MessageComposer.DanceMessageComposer)
        );
        builder.MapSerializer(
            typeof(ExpressionMessageComposer),
            new ExpressionMessageComposerSerializer(MessageComposer.ExpressionMessageComposer)
        );
        builder.MapSerializer(
            typeof(SleepMessageComposer),
            new SleepMessageComposerSerializer(MessageComposer.SleepMessageComposer)
        );
        builder.MapSerializer(
            typeof(UseObjectMessageComposer),
            new UseObjectMessageComposerSerializer(MessageComposer.UseObjectMessageComposer)
        );

        // Room Bots
        builder.MapSerializer(
            typeof(BotCommandConfigurationMessageComposer),
            new BotCommandConfigurationMessageComposerSerializer(
                MessageComposer.BotCommandConfigurationComposer
            )
        );
        builder.MapSerializer(
            typeof(BotErrorMessageComposer),
            new BotErrorMessageComposerSerializer(MessageComposer.BotErrorComposer)
        );
        builder.MapSerializer(
            // The composer, not its serializer: the map is keyed by what a caller sends, and keying
            // it by the serializer left this one unreachable.
            typeof(BotForceOpenContextMenuMessageComposer),
            new BotForceOpenContextMenuMessageComposerSerializer(
                MessageComposer.BotForceOpenContextMenuComposer
            )
        );
        builder.MapSerializer(
            typeof(BotSkillListUpdateMessageComposer),
            new BotSkillListUpdateMessageComposerSerializer(
                MessageComposer.BotSkillListUpdateComposer
            )
        );

        // Room Chat
        builder.MapSerializer(
            typeof(ChatMessageComposer),
            new ChatMessageComposerSerializer(MessageComposer.ChatMessageComposer)
        );
        builder.MapSerializer(
            typeof(FloodControlMessageComposer),
            new FloodControlMessageComposerSerializer(MessageComposer.FloodControlMessageComposer)
        );
        builder.MapSerializer(
            typeof(RemainingMutePeriodMessageComposer),
            new RemainingMutePeriodMessageComposerSerializer(
                MessageComposer.RemainingMutePeriodComposer
            )
        );
        builder.MapSerializer(
            typeof(RoomChatSettingsMessageComposer),
            new RoomChatSettingsMessageComposerSerializer(
                MessageComposer.RoomChatSettingsMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RoomFilterSettingsMessageComposer),
            new RoomFilterSettingsMessageComposerSerializer(
                MessageComposer.RoomFilterSettingsMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ShoutMessageComposer),
            new ShoutMessageComposerSerializer(MessageComposer.ShoutMessageComposer)
        );
        builder.MapSerializer(
            typeof(UserTypingMessageComposer),
            new UserTypingMessageComposerSerializer(MessageComposer.UserTypingMessageComposer)
        );
        builder.MapSerializer(
            typeof(WhisperMessageComposer),
            new WhisperMessageComposerSerializer(MessageComposer.WhisperMessageComposer)
        );

        // Room Engine
        builder.MapSerializer(
            typeof(FavoriteMembershipUpdateMessageComposer),
            new FavoriteMembershipUpdateMessageComposerSerializer(
                MessageComposer.FavoriteMembershipUpdateMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(FloorHeightMapMessageComposer),
            new FloorHeightMapMessageComposerSerializer(
                MessageComposer.FloorHeightMapMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(FurnitureAliasesMessageComposer),
            new FurnitureAliasesMessageComposerSerializer(
                MessageComposer.FurnitureAliasesMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(HeightMapMessageComposer),
            new HeightMapMessageComposerSerializer(MessageComposer.HeightMapMessageComposer)
        );
        builder.MapSerializer(
            typeof(HeightMapUpdateMessageComposer),
            new HeightMapUpdateMessageComposerSerializer(
                MessageComposer.HeightMapUpdateMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ItemAddMessageComposer),
            new ItemAddMessageComposerSerializer(MessageComposer.ItemAddMessageComposer)
        );
        builder.MapSerializer(
            typeof(ItemDataUpdateMessageComposer),
            new ItemDataUpdateMessageComposerSerializer(
                MessageComposer.ItemDataUpdateMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ItemRemoveMessageComposer),
            new ItemRemoveMessageComposerSerializer(MessageComposer.ItemRemoveMessageComposer)
        );
        builder.MapSerializer(
            typeof(ItemsMessageComposer),
            new ItemsMessageComposerSerializer(MessageComposer.ItemsMessageComposer)
        );
        builder.MapSerializer(
            typeof(ItemsStateUpdateMessageComposer),
            new ItemsStateUpdateMessageComposerSerializer(
                MessageComposer.ItemsStateUpdateMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ItemStateUpdateMessageComposer),
            new ItemStateUpdateMessageComposerSerializer(
                MessageComposer.ItemStateUpdateMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ItemUpdateMessageComposer),
            new ItemUpdateMessageComposerSerializer(MessageComposer.ItemUpdateMessageComposer)
        );
        builder.MapSerializer(
            typeof(ObjectAddMessageComposer),
            new ObjectAddMessageComposerSerializer(MessageComposer.ObjectAddMessageComposer)
        );
        builder.MapSerializer(
            typeof(ObjectDataUpdateMessageComposer),
            new ObjectDataUpdateMessageComposerSerializer(
                MessageComposer.ObjectDataUpdateMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ObjectRemoveConfirmMessageComposer),
            new ObjectRemoveConfirmMessageComposerSerializer(
                MessageComposer.ObjectRemoveConfirmMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ObjectRemoveMessageComposer),
            new ObjectRemoveMessageComposerSerializer(MessageComposer.ObjectRemoveMessageComposer)
        );
        builder.MapSerializer(
            typeof(ObjectRemoveMultipleMessageComposer),
            new ObjectRemoveMultipleMessageComposerSerializer(
                MessageComposer.ObjectRemoveMultipleMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ObjectsDataUpdateMessageComposer),
            new ObjectsDataUpdateMessageComposerSerializer(
                MessageComposer.ObjectsDataUpdateMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ObjectsMessageComposer),
            new ObjectsMessageComposerSerializer(MessageComposer.ObjectsMessageComposer)
        );
        builder.MapSerializer(
            typeof(ObjectUpdateMessageComposer),
            new ObjectUpdateMessageComposerSerializer(MessageComposer.ObjectUpdateMessageComposer)
        );
        builder.MapSerializer(
            typeof(VortexFurniEditorDataMessageComposer),
            new VortexFurniEditorDataMessageComposerSerializer(
                MessageComposer.VortexFurniEditorDataMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(VortexFurniEditorRightsMessageComposer),
            new VortexFurniEditorRightsMessageComposerSerializer(
                MessageComposer.VortexFurniEditorRightsMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(VortexFurniDefinitionMessageComposer),
            new VortexFurniDefinitionMessageComposerSerializer(
                MessageComposer.VortexFurniDefinitionMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RoomEntryInfoMessageComposer),
            new RoomEntryInfoMessageComposerSerializer(MessageComposer.RoomEntryInfoMessageComposer)
        );
        builder.MapSerializer(
            typeof(RoomPropertyMessageComposer),
            new RoomPropertyMessageComposerSerializer(MessageComposer.RoomPropertyMessageComposer)
        );
        builder.MapSerializer(
            typeof(RoomVisualizationSettingsMessageComposer),
            new RoomVisualizationSettingsMessageComposerSerializer(
                MessageComposer.RoomVisualizationSettingsComposer
            )
        );
        builder.MapSerializer(
            typeof(SlideObjectBundleMessageComposer),
            new SlideObjectBundleMessageComposerSerializer(
                MessageComposer.SlideObjectBundleMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(SpecialRoomEffectMessageComposer),
            new SpecialRoomEffectMessageComposerSerializer(
                MessageComposer.SpecialRoomEffectMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(UserChangeMessageComposer),
            new UserChangeMessageComposerSerializer(MessageComposer.UserChangeMessageComposer)
        );
        builder.MapSerializer(
            typeof(UserRemoveMessageComposer),
            new UserRemoveMessageComposerSerializer(MessageComposer.UserRemoveMessageComposer)
        );
        builder.MapSerializer(
            typeof(UsersMessageComposer),
            new UsersMessageComposerSerializer(MessageComposer.UsersMessageComposer)
        );
        builder.MapSerializer(
            typeof(UserUpdateMessageComposer),
            new UserUpdateMessageComposerSerializer(MessageComposer.UserUpdateMessageComposer)
        );

        // Room Furniture
        builder.MapSerializer(
            typeof(AreaHideMessageComposer),
            new AreaHideMessageComposerSerializer(MessageComposer.AreaHideMessageComposer)
        );
        builder.MapSerializer(
            typeof(CustomStackingHeightUpdateMessageComposer),
            new CustomStackingHeightUpdateMessageComposerSerializer(
                MessageComposer.CustomStackingHeightUpdateMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(CustomUserNotificationMessageComposer),
            new CustomUserNotificationMessageComposerSerializer(
                MessageComposer.CustomUserNotificationMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(DiceValueMessageComposer),
            new DiceValueMessageComposerSerializer(MessageComposer.DiceValueMessageComposer)
        );
        builder.MapSerializer(
            typeof(FurniRentOrBuyoutOfferMessageComposer),
            new FurniRentOrBuyoutOfferMessageComposerSerializer(
                MessageComposer.FurniRentOrBuyoutOfferMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuildFurniContextMenuInfoMessageComposer),
            new GuildFurniContextMenuInfoMessageComposerSerializer(
                MessageComposer.GuildFurniContextMenuInfoMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(OneWayDoorStatusMessageComposer),
            new OneWayDoorStatusMessageComposerSerializer(
                MessageComposer.OneWayDoorStatusMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(OpenPetPackageRequestedMessageComposer),
            new OpenPetPackageRequestedMessageComposerSerializer(
                MessageComposer.OpenPetPackageRequestedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(OpenPetPackageResultMessageComposer),
            new OpenPetPackageResultMessageComposerSerializer(
                MessageComposer.OpenPetPackageResultMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(PresentOpenedMessageComposer),
            new PresentOpenedMessageComposerSerializer(MessageComposer.PresentOpenedMessageComposer)
        );
        builder.MapSerializer(
            typeof(RentableSpaceConfigMessageComposer),
            new RentableSpaceConfigMessageComposerSerializer(
                MessageComposer.RentableSpaceConfigMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RentableSpaceRentFailedMessageComposer),
            new RentableSpaceRentFailedMessageComposerSerializer(
                MessageComposer.RentableSpaceRentFailedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RentableSpaceRentOkMessageComposer),
            new RentableSpaceRentOkMessageComposerSerializer(
                MessageComposer.RentableSpaceRentOkMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RentableSpaceStatusMessageComposer),
            new RentableSpaceStatusMessageComposerSerializer(
                MessageComposer.RentableSpaceStatusMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RequestSpamWallPostItMessageComposer),
            new RequestSpamWallPostItMessageComposerSerializer(
                MessageComposer.RequestSpamWallPostItMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RoomDimmerPresetsMessageComposer),
            new RoomDimmerPresetsMessageComposerSerializer(
                MessageComposer.RoomDimmerPresetsMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RoomMessageNotificationMessageComposer),
            new RoomMessageNotificationMessageComposerSerializer(
                MessageComposer.RoomMessageNotificationMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(YoutubeControlVideoMessageComposer),
            new YoutubeControlVideoMessageComposerSerializer(
                MessageComposer.YoutubeControlVideoMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(YoutubeDisplayPlaylistsMessageComposer),
            new YoutubeDisplayPlaylistsMessageComposerSerializer(
                MessageComposer.YoutubeDisplayPlaylistsMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(YoutubeDisplayVideoMessageComposer),
            new YoutubeDisplayVideoMessageComposerSerializer(
                MessageComposer.YoutubeDisplayVideoMessageComposer
            )
        );

        // Room Layout
        builder.MapSerializer(
            typeof(RoomEntryTileMessageComposer),
            new RoomEntryTileMessageComposerSerializer(MessageComposer.RoomEntryTileMessageComposer)
        );
        builder.MapSerializer(
            typeof(RoomOccupiedTilesMessageComposer),
            new RoomOccupiedTilesMessageComposerSerializer(
                MessageComposer.RoomOccupiedTilesMessageComposer
            )
        );

        // Room Permissions
        builder.MapSerializer(
            typeof(YouAreControllerMessageComposer),
            new YouAreControllerMessageComposerSerializer(
                MessageComposer.YouAreControllerMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(YouAreNotControllerMessageComposer),
            new YouAreNotControllerMessageComposerSerializer(
                MessageComposer.YouAreNotControllerMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(YouAreOwnerMessageComposer),
            new YouAreOwnerMessageComposerSerializer(MessageComposer.YouAreOwnerMessageComposer)
        );

        // Room Pets
        builder.MapSerializer(
            typeof(PetCommandsMessageComposer),
            new PetCommandsMessageComposerSerializer(MessageComposer.PetCommandsMessageComposer)
        );
        builder.MapSerializer(
            typeof(PetExperienceMessageComposer),
            new PetExperienceMessageComposerSerializer(MessageComposer.PetExperienceComposer)
        );
        builder.MapSerializer(
            typeof(PetFigureUpdateMessageComposer),
            new PetFigureUpdateMessageComposerSerializer(MessageComposer.PetFigureUpdateComposer)
        );
        builder.MapSerializer(
            typeof(PetInfoMessageComposer),
            new PetInfoMessageComposerSerializer(MessageComposer.PetInfoMessageComposer)
        );
        builder.MapSerializer(
            typeof(PetLevelUpdateMessageComposer),
            new PetLevelUpdateMessageComposerSerializer(MessageComposer.PetLevelUpdateComposer)
        );
        builder.MapSerializer(
            typeof(PetPlacingErrorMessageComposer),
            new PetPlacingErrorMessageComposerSerializer(MessageComposer.PetPlacingErrorComposer)
        );
        builder.MapSerializer(
            typeof(PetRespectFailedMessageComposer),
            new PetRespectFailedMessageComposerSerializer(MessageComposer.PetRespectFailedComposer)
        );
        builder.MapSerializer(
            typeof(PetStatusUpdateMessageComposer),
            new PetStatusUpdateMessageComposerSerializer(MessageComposer.PetStatusUpdateComposer)
        );

        // Room Session
        builder.MapSerializer(
            typeof(CantConnectMessageComposer),
            new CantConnectMessageComposerSerializer(MessageComposer.CantConnectMessageComposer)
        );
        builder.MapSerializer(
            typeof(CloseConnectionMessageComposer),
            new CloseConnectionMessageComposerSerializer(
                MessageComposer.CloseConnectionMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(FlatAccessibleMessageComposer),
            new FlatAccessibleMessageComposerSerializer(
                MessageComposer.FlatAccessibleMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GamePlayerValueMessageComposer),
            new GamePlayerValueMessageComposerSerializer(
                MessageComposer.GamePlayerValueMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(HanditemConfigurationMessageComposer),
            new HanditemConfigurationMessageComposerSerializer(
                MessageComposer.HanditemConfigurationMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(OpenConnectionMessageComposer),
            new OpenConnectionMessageComposerSerializer(
                MessageComposer.OpenConnectionMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RoomForwardMessageComposer),
            new RoomForwardMessageComposerSerializer(MessageComposer.RoomForwardMessageComposer)
        );
        builder.MapSerializer(
            typeof(RoomQueueStatusMessageComposer),
            new RoomQueueStatusMessageComposerSerializer(
                MessageComposer.RoomQueueStatusMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RoomReadyMessageComposer),
            new RoomReadyMessageComposerSerializer(MessageComposer.RoomReadyMessageComposer)
        );
        builder.MapSerializer(
            typeof(YouAreNotSpectatorMessageComposer),
            new YouAreNotSpectatorMessageComposerSerializer(
                MessageComposer.YouAreNotSpectatorMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(YouArePlayingGameMessageComposer),
            new YouArePlayingGameMessageComposerSerializer(
                MessageComposer.YouArePlayingGameMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(YouAreSpectatorMessageComposer),
            new YouAreSpectatorMessageComposerSerializer(
                MessageComposer.YouAreSpectatorMessageComposer
            )
        );
    }
}

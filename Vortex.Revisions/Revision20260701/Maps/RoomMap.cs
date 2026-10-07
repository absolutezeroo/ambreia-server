using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Room.Action;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Room.Layout;
using Vortex.Protocol.Messages.Outgoing.Room.Permissions;
using Vortex.Protocol.Messages.Outgoing.Room.Session;
using Vortex.Revisions.Revision20260701.Parsers.Room.Avatar;
using Vortex.Revisions.Revision20260701.Parsers.Room.Engine;
using Vortex.Revisions.Revision20260701.Parsers.Room.Session;
using Vortex.Revisions.Revision20260701.Serializers.Room.Action;
using Vortex.Revisions.Revision20260701.Serializers.Room.Engine;
using Vortex.Revisions.Revision20260701.Serializers.Room.Layout;
using Vortex.Revisions.Revision20260701.Serializers.Room.Permissions;
using Vortex.Revisions.Revision20260701.Serializers.Room.Session;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class RoomMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        // Room Action

        // Room Avatar
        builder.MapParser(MessageEvent.LookToMessageEvent, new LookToMessageParser());

        // Room Engine
        builder.MapParser(
            MessageEvent.GetFurnitureAliasesMessageEvent,
            new GetFurnitureAliasesMessageParser()
        );
        builder.MapParser(MessageEvent.MoveAvatarMessageEvent, new MoveAvatarMessageParser());

        // Room Furniture

        // Room Layout

        // Room Session
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

        // Room Engine
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
            typeof(ObjectRemoveMessageComposer),
            new ObjectRemoveMessageComposerSerializer(MessageComposer.ObjectRemoveMessageComposer)
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

        // Room Layout
        builder.MapSerializer(
            typeof(RoomEntryTileMessageComposer),
            new RoomEntryTileMessageComposerSerializer(MessageComposer.RoomEntryTileMessageComposer)
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
            typeof(RoomReadyMessageComposer),
            new RoomReadyMessageComposerSerializer(MessageComposer.RoomReadyMessageComposer)
        );
    }
}

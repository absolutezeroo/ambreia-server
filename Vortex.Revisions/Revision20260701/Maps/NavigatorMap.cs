using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Navigator;
using Vortex.Revisions.Revision20260701.Parsers.Navigator;
using Vortex.Revisions.Revision20260701.Serializers.Navigator;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class NavigatorMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(MessageEvent.CanCreateRoomMessageEvent, new CanCreateRoomMessageParser());
        builder.MapParser(
            MessageEvent.ConvertGlobalRoomIdMessageEvent,
            new ConvertGlobalRoomIdMessageParser()
        );
        builder.MapParser(MessageEvent.CreateFlatMessageEvent, new CreateFlatMessageParser());
        builder.MapParser(
            MessageEvent.ForwardToSomeRoomMessageEvent,
            new ForwardToSomeRoomMessageParser()
        );
        builder.MapParser(MessageEvent.GetGuestRoomMessageEvent, new GetGuestRoomMessageParser());
        builder.MapParser(
            MessageEvent.GetUserFlatCatsMessageEvent,
            new GetUserFlatCatsMessageParser()
        );
        builder.MapParser(
            MessageEvent.GuildBaseSearchMessageEvent,
            new GuildBaseSearchMessageParser()
        );
        builder.MapParser(
            MessageEvent.MyFriendsRoomsSearchMessageEvent,
            new MyFriendsRoomsSearchMessageParser()
        );
        builder.MapParser(
            MessageEvent.MyGuildBasesSearchMessageEvent,
            new MyGuildBasesSearchMessageParser()
        );
        builder.MapParser(MessageEvent.MyRoomsSearchMessageEvent, new MyRoomsSearchMessageParser());
        builder.MapParser(
            MessageEvent.RemoveOwnRoomRightsRoomMessageEvent,
            new RemoveOwnRoomRightsRoomMessageParser()
        );
        builder.MapParser(
            MessageEvent.SetRoomSessionTagsMessageEvent,
            new SetRoomSessionTagsMessageParser()
        );
        builder.MapParser(
            MessageEvent.UpdateHomeRoomMessageEvent,
            new UpdateHomeRoomMessageParser()
        );

        builder.MapSerializer(
            typeof(CanCreateRoomMessageComposer),
            new CanCreateRoomMessageComposerSerializer(MessageComposer.CanCreateRoomComposer)
        );
        builder.MapSerializer(
            typeof(DoorbellMessageComposer),
            new DoorbellMessageComposerSerializer(MessageComposer.DoorbellMessageComposer)
        );
        builder.MapSerializer(
            typeof(FavouritesMessageComposer),
            new FavouritesMessageSerializer(MessageComposer.FavouritesComposer)
        );
        builder.MapSerializer(
            typeof(FlatAccessDeniedMessageComposer),
            new FlatAccessDeniedMessageComposerSerializer(
                MessageComposer.FlatAccessDeniedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(FlatCreatedMessageComposer),
            new FlatCreatedMessageComposerSerializer(MessageComposer.FlatCreatedComposer)
        );
        builder.MapSerializer(
            typeof(GetGuestRoomResultMessageComposer),
            new GetGuestRoomResultMessageComposerSerializer(
                MessageComposer.GetGuestRoomResultComposer
            )
        );
        builder.MapSerializer(
            typeof(NavigatorSettingsMessageComposer),
            new NavigatorSettingsMessageComposerSerializer(
                MessageComposer.NavigatorSettingsComposer
            )
        );
        builder.MapSerializer(
            typeof(RoomRatingMessageComposer),
            new RoomRatingMessageComposerSerializer(MessageComposer.RoomRatingComposer)
        );
        builder.MapSerializer(
            typeof(UserFlatCatsMessageComposer),
            new UserFlatCatsMessageComposerSerializer(MessageComposer.UserFlatCatsComposer)
        );
    }
}

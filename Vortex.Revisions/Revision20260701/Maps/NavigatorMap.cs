using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Navigator;
using Vortex.Revisions.Revision20260701.Parsers.Navigator;
using Vortex.Revisions.Revision20260701.Serializers.Navigator;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class NavigatorMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(
            MessageEvent.AddFavouriteRoomMessageEvent,
            new AddFavouriteRoomMessageParser()
        );
        builder.MapParser(MessageEvent.CancelEventMessageEvent, new CancelEventMessageParser());
        builder.MapParser(MessageEvent.CanCreateRoomMessageEvent, new CanCreateRoomMessageParser());
        builder.MapParser(
            MessageEvent.CompetitionRoomsSearchMessageEvent,
            new CompetitionRoomsSearchMessageParser()
        );
        builder.MapParser(
            MessageEvent.ConvertGlobalRoomIdMessageEvent,
            new ConvertGlobalRoomIdMessageParser()
        );
        builder.MapParser(MessageEvent.CreateFlatMessageEvent, new CreateFlatMessageParser());
        builder.MapParser(
            MessageEvent.DeleteFavouriteRoomMessageEvent,
            new DeleteFavouriteRoomMessageParser()
        );
        builder.MapParser(MessageEvent.EditEventMessageEvent, new EditEventMessageParser());
        builder.MapParser(
            MessageEvent.ForwardToARandomPromotedRoomMessageEvent,
            new ForwardToARandomPromotedRoomMessageParser()
        );
        builder.MapParser(
            MessageEvent.ForwardToSomeRoomMessageEvent,
            new ForwardToSomeRoomMessageParser()
        );
        builder.MapParser(MessageEvent.GetGuestRoomMessageEvent, new GetGuestRoomMessageParser());
        builder.MapParser(
            MessageEvent.GetOfficialRoomsMessageEvent,
            new GetOfficialRoomsMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetPopularRoomTagsMessageEvent,
            new GetPopularRoomTagsMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetUserEventCatsMessageEvent,
            new GetUserEventCatsMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetUserFlatCatsMessageEvent,
            new GetUserFlatCatsMessageParser()
        );
        builder.MapParser(
            MessageEvent.GuildBaseSearchMessageEvent,
            new GuildBaseSearchMessageParser()
        );
        builder.MapParser(
            MessageEvent.MyFavouriteRoomsSearchMessageEvent,
            new MyFavouriteRoomsSearchMessageParser()
        );
        builder.MapParser(
            MessageEvent.MyFrequentRoomHistorySearchMessageEvent,
            new MyFrequentRoomHistorySearchMessageParser()
        );
        builder.MapParser(
            MessageEvent.MyFriendsRoomsSearchMessageEvent,
            new MyFriendsRoomsSearchMessageParser()
        );
        builder.MapParser(
            MessageEvent.MyGuildBasesSearchMessageEvent,
            new MyGuildBasesSearchMessageParser()
        );
        builder.MapParser(
            MessageEvent.MyRecommendedRoomsMessageEvent,
            new MyRecommendedRoomsMessageParser()
        );
        builder.MapParser(
            MessageEvent.MyRoomHistorySearchMessageEvent,
            new MyRoomHistorySearchMessageParser()
        );
        builder.MapParser(
            MessageEvent.MyRoomRightsSearchMessageEvent,
            new MyRoomRightsSearchMessageParser()
        );
        builder.MapParser(MessageEvent.MyRoomsSearchMessageEvent, new MyRoomsSearchMessageParser());
        builder.MapParser(
            MessageEvent.PopularRoomsSearchMessageEvent,
            new PopularRoomsSearchMessageParser()
        );
        builder.MapParser(
            MessageEvent.RemoveOwnRoomRightsRoomMessageEvent,
            new RemoveOwnRoomRightsRoomMessageParser()
        );
        builder.MapParser(
            MessageEvent.RoomAdEventTabAdClickedEvent,
            new RoomAdEventTabAdClickedMessageParser()
        );
        builder.MapParser(
            MessageEvent.RoomAdEventTabViewedEvent,
            new RoomAdEventTabViewedMessageParser()
        );
        builder.MapParser(MessageEvent.RoomAdSearchMessageEvent, new RoomAdSearchMessageParser());
        builder.MapParser(
            MessageEvent.RoomsWhereMyFriendsAreSearchMessageEvent,
            new RoomsWhereMyFriendsAreSearchMessageParser()
        );
        builder.MapParser(
            MessageEvent.RoomsWithHighestScoreSearchMessageEvent,
            new RoomsWithHighestScoreSearchMessageParser()
        );
        builder.MapParser(
            MessageEvent.RoomTextSearchMessageEvent,
            new RoomTextSearchMessageParser()
        );
        builder.MapParser(
            MessageEvent.SetRoomSessionTagsMessageEvent,
            new SetRoomSessionTagsMessageParser()
        );
        builder.MapParser(
            MessageEvent.ToggleStaffPickMessageEvent,
            new ToggleStaffPickMessageParser()
        );
        builder.MapParser(
            MessageEvent.UpdateHomeRoomMessageEvent,
            new UpdateHomeRoomMessageParser()
        );

        builder.MapSerializer(
            typeof(CanCreateRoomEventMessageComposer),
            new CanCreateRoomEventMessageComposerSerializer(
                MessageComposer.CanCreateRoomEventComposer
            )
        );
        builder.MapSerializer(
            typeof(CanCreateRoomMessageComposer),
            new CanCreateRoomMessageComposerSerializer(MessageComposer.CanCreateRoomComposer)
        );
        builder.MapSerializer(
            typeof(CategoriesWithVisitorCountMessageComposer),
            new CategoriesWithVisitorCountMessageComposerSerializer(
                MessageComposer.CategoriesWithVisitorCountComposer
            )
        );
        builder.MapSerializer(
            typeof(CompetitionRoomsDataMessageComposer),
            new CompetitionRoomsDataMessageComposerSerializer(
                MessageComposer.CompetitionRoomsDataMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ConvertedRoomIdMessageComposer),
            new ConvertedRoomIdMessageComposerSerializer(MessageComposer.ConvertedRoomIdComposer)
        );
        builder.MapSerializer(
            typeof(DoorbellMessageComposer),
            new DoorbellMessageComposerSerializer(MessageComposer.DoorbellMessageComposer)
        );
        builder.MapSerializer(
            typeof(FavouriteChangedMessageComposer),
            new FavouriteChangedMessageComposerSerializer(MessageComposer.FavouriteChangedComposer)
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
            typeof(GuestRoomSearchResultMessageComposer),
            new GuestRoomSearchResultMessageComposerSerializer(
                MessageComposer.GuestRoomSearchResultComposer
            )
        );
        builder.MapSerializer(
            typeof(NavigatorSettingsMessageComposer),
            new NavigatorSettingsMessageComposerSerializer(
                MessageComposer.NavigatorSettingsComposer
            )
        );
        builder.MapSerializer(
            typeof(OfficialRoomsMessageComposer),
            new OfficialRoomsMessageComposerSerializer(MessageComposer.OfficialRoomsComposer)
        );
        builder.MapSerializer(
            typeof(PopularRoomTagsResultMessageComposer),
            new PopularRoomTagsResultMessageComposerSerializer(
                MessageComposer.PopularRoomTagsResultComposer
            )
        );
        builder.MapSerializer(
            typeof(RoomEventCancelMessageComposer),
            new RoomEventCancelMessageComposerSerializer(MessageComposer.RoomEventCancelComposer)
        );
        builder.MapSerializer(
            typeof(RoomEventMessageComposer),
            new RoomEventMessageComposerSerializer(MessageComposer.RoomEventComposer)
        );
        builder.MapSerializer(
            typeof(RoomInfoUpdatedMessageComposer),
            new RoomInfoUpdatedMessageComposerSerializer(MessageComposer.RoomInfoUpdatedComposer)
        );
        builder.MapSerializer(
            typeof(RoomRatingMessageComposer),
            new RoomRatingMessageComposerSerializer(MessageComposer.RoomRatingComposer)
        );
        builder.MapSerializer(
            typeof(UserEventCatsMessageComposer),
            new UserEventCatsMessageComposerSerializer(MessageComposer.UserEventCatsComposer)
        );
        builder.MapSerializer(
            typeof(UserFlatCatsMessageComposer),
            new UserFlatCatsMessageComposerSerializer(MessageComposer.UserFlatCatsComposer)
        );
    }
}

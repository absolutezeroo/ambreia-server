using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.FriendList;
using Vortex.Revisions.Configuration;
using Vortex.Revisions.Revision20260701.Parsers.FriendList;
using Vortex.Revisions.Revision20260701.Serializers.FriendList;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class FriendListMap : IRevisionMap
{
    private readonly ProtocolLimitsConfig _protocolLimits;

    public FriendListMap(ProtocolLimitsConfig protocolLimits)
    {
        _protocolLimits = protocolLimits;
    }

    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(MessageEvent.AcceptFriendMessageEvent, new AcceptFriendMessageParser());
        builder.MapParser(MessageEvent.DeclineFriendMessageEvent, new DeclineFriendMessageParser());
        builder.MapParser(
            MessageEvent.FindNewFriendsMessageEvent,
            new FindNewFriendsMessageParser()
        );
        builder.MapParser(MessageEvent.FollowFriendMessageEvent, new FollowFriendMessageParser());
        builder.MapParser(
            MessageEvent.FriendListUpdateMessageEvent,
            new FriendListUpdateMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetFriendRequestsMessageEvent,
            new GetFriendRequestsMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetMessengerHistoryEvent,
            new GetMessengerHistoryMessageParser()
        );
        builder.MapParser(MessageEvent.HabboSearchMessageEvent, new HabboSearchMessageParser());
        builder.MapParser(MessageEvent.MessengerInitMessageEvent, new MessengerInitMessageParser());
        builder.MapParser(
            MessageEvent.RemoveFriendMessageEvent,
            new RemoveFriendMessageParser(_protocolLimits.MaxFriendRemovalIds)
        );
        builder.MapParser(MessageEvent.RequestFriendMessageEvent, new RequestFriendMessageParser());
        builder.MapParser(MessageEvent.SendMsgMessageEvent, new SendMsgMessageParser());
        builder.MapParser(
            MessageEvent.SetRelationshipStatusMessageEvent,
            new SetRelationshipStatusMessageParser()
        );
        builder.MapParser(
            MessageEvent.SendRoomInviteMessageEvent,
            new SendRoomInviteMessageParser()
        );
        builder.MapParser(MessageEvent.VisitUserMessageEvent, new VisitUserMessageParser());

        builder.MapSerializer(
            typeof(AcceptFriendResultMessageComposer),
            new AcceptFriendResultMessageSerializer(MessageComposer.AcceptFriendResultComposer)
        );
        builder.MapSerializer(
            typeof(ConsoleMessageHistoryMessageComposer),
            new ConsoleMessageHistoryMessageSerializer(
                MessageComposer.ConsoleMessageHistoryComposer
            )
        );
        builder.MapSerializer(
            typeof(FollowFriendFailedMessageComposer),
            new FollowFriendFailedMessageSerializer(MessageComposer.FollowFriendFailedComposer)
        );
        builder.MapSerializer(
            typeof(FriendListFragmentMessageComposer),
            new FriendListFragmentMessageSerializer(
                MessageComposer.FriendListFragmentMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(FriendListUpdateMessageComposer),
            new FriendListUpdateMessageSerializer(MessageComposer.FriendListUpdateComposer)
        );
        builder.MapSerializer(
            typeof(FriendNotificationMessageComposer),
            new FriendNotificationMessageSerializer(MessageComposer.FriendNotificationComposer)
        );
        builder.MapSerializer(
            typeof(FriendRequestsMessageComposer),
            new FriendRequestsMessageSerializer(MessageComposer.FriendRequestsComposer)
        );
        builder.MapSerializer(
            typeof(HabboSearchResultMessageComposer),
            new HabboSearchResultMessageSerializer(MessageComposer.HabboSearchResultComposer)
        );
        builder.MapSerializer(
            typeof(InstantMessageErrorMessageComposer),
            new InstantMessageErrorMessageSerializer(MessageComposer.InstantMessageErrorComposer)
        );
        builder.MapSerializer(
            typeof(MessengerErrorMessageComposer),
            new MessengerErrorMessageSerializer(MessageComposer.MessengerErrorComposer)
        );
        builder.MapSerializer(
            typeof(MessengerInitMessageComposer),
            new MessengerInitMessageSerializer(MessageComposer.MessengerInitComposer)
        );
        builder.MapSerializer(
            typeof(MiniMailNewMessageComposer),
            new MiniMailNewMessageSerializer(MessageComposer.MiniMailNewMessageComposer)
        );
        builder.MapSerializer(
            typeof(MiniMailUnreadCountMessageComposer),
            new MiniMailUnreadCountMessageSerializer(MessageComposer.MiniMailUnreadCountComposer)
        );
        builder.MapSerializer(
            typeof(NewConsoleMessageMessageComposer),
            new NewConsoleMessageMessageSerializer(MessageComposer.NewConsoleMessageComposer)
        );
        builder.MapSerializer(
            typeof(NewFriendRequestMessageComposer),
            new NewFriendRequestMessageSerializer(MessageComposer.NewFriendRequestComposer)
        );
        builder.MapSerializer(
            typeof(RoomInviteErrorMessageComposer),
            new RoomInviteErrorMessageSerializer(MessageComposer.RoomInviteErrorComposer)
        );
        builder.MapSerializer(
            typeof(RoomInviteMessageComposer),
            new RoomInviteMessageSerializer(MessageComposer.RoomInviteComposer)
        );
        builder.MapSerializer(
            typeof(FindFriendsProcessResultMessageComposer),
            new FindFriendsProcessResultMessageComposerSerializer(
                MessageComposer.FindFriendsProcessResultComposer
            )
        );
    }
}

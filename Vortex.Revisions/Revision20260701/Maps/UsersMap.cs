using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Users;
using Vortex.Revisions.Revision20260701.Parsers.Users;
using Vortex.Revisions.Revision20260701.Serializers.Users;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class UsersMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(
            MessageEvent.AddAdminRightsToMemberMessageEvent,
            new AddAdminRightsToMemberMessageParser()
        );
        builder.MapParser(
            MessageEvent.ApproveAllMembershipRequestsMessageEvent,
            new ApproveAllMembershipRequestsMessageParser()
        );
        builder.MapParser(
            MessageEvent.ApproveMembershipRequestMessageEvent,
            new ApproveMembershipRequestMessageParser()
        );
        builder.MapParser(MessageEvent.ApproveNameMessageEvent, new ApproveNameMessageParser());
        builder.MapParser(MessageEvent.ChangeEmailEvent, new ChangeEmailMessageParser());
        builder.MapParser(MessageEvent.CreateGuildMessageEvent, new CreateGuildMessageParser());
        builder.MapParser(
            MessageEvent.DeactivateGuildMessageEvent,
            new DeactivateGuildMessageParser()
        );
        builder.MapParser(
            MessageEvent.DeselectFavouriteHabboGroupMessageEvent,
            new DeselectFavouriteHabboGroupMessageParser()
        );
        builder.MapParser(MessageEvent.GetEmailStatusEvent, new GetEmailStatusMessageParser());
        builder.MapParser(
            MessageEvent.GetExtendedProfileByNameMessageEvent,
            new GetExtendedProfileByNameMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetExtendedProfileMessageEvent,
            new GetExtendedProfileMessageParser()
        );
        builder.MapParser(MessageEvent.RespectUserMessageEvent, new RespectUserMessageParser());
        builder.MapParser(
            MessageEvent.ReplenishRespectMessageEvent,
            new ReplenishRespectMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetGuildCreationInfoMessageEvent,
            new GetGuildCreationInfoMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetGuildEditInfoMessageEvent,
            new GetGuildEditInfoMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetGuildEditorDataMessageEvent,
            new GetGuildEditorDataMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetGuildMembershipsMessageEvent,
            new GetGuildMembershipsMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetGuildMembersMessageEvent,
            new GetGuildMembersMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetHabboGroupBadgesMessageEvent,
            new GetHabboGroupBadgesMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetHabboGroupDetailsMessageEvent,
            new GetHabboGroupDetailsMessageParser()
        );
        builder.MapParser(MessageEvent.BlockListInitEvent, new BlockListInitMessageParser());
        builder.MapParser(
            MessageEvent.GetIgnoredUsersMessageEvent,
            new GetIgnoredUsersMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetMemberGuildItemCountMessageEvent,
            new GetMemberGuildItemCountMessageParser()
        );
        builder.MapParser(MessageEvent.GetMOTDMessageEvent, new GetMOTDMessageParser());
        builder.MapParser(
            MessageEvent.GetRelationshipStatusInfoMessageEvent,
            new GetRelationshipStatusInfoMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetSelectedBadgesMessageEvent,
            new GetSelectedBadgesMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetUserNftChatStylesMessageEvent,
            new GetUserNftChatStylesMessageParser()
        );
        builder.MapParser(MessageEvent.IgnoreUserMessageEvent, new IgnoreUserMessageParser());
        builder.MapParser(
            MessageEvent.JoinHabboGroupMessageEvent,
            new JoinHabboGroupMessageParser()
        );
        builder.MapParser(MessageEvent.KickMemberMessageEvent, new KickMemberMessageParser());
        builder.MapParser(
            MessageEvent.RejectMembershipRequestMessageEvent,
            new RejectMembershipRequestMessageParser()
        );
        builder.MapParser(
            MessageEvent.RemoveAdminRightsFromMemberMessageEvent,
            new RemoveAdminRightsFromMemberMessageParser()
        );
        builder.MapParser(
            MessageEvent.ScrGetKickbackInfoMessageEvent,
            new ScrGetKickbackInfoMessageParser()
        );
        builder.MapParser(
            MessageEvent.ScrGetUserInfoMessageEvent,
            new ScrGetUserInfoMessageParser()
        );
        builder.MapParser(
            MessageEvent.SelectFavouriteHabboGroupMessageEvent,
            new SelectFavouriteHabboGroupMessageParser()
        );
        builder.MapParser(MessageEvent.BlockUserMessageEvent, new BlockUserMessageParser());
        builder.MapParser(
            MessageEvent.UnblockGroupMemberMessageEvent,
            new UnblockGroupMemberMessageParser()
        );
        builder.MapParser(MessageEvent.UnblockUserMessageEvent, new UnblockUserMessageParser());
        builder.MapParser(MessageEvent.UnignoreUserMessageEvent, new UnignoreUserMessageParser());
        builder.MapParser(
            MessageEvent.UpdateGuildBadgeMessageEvent,
            new UpdateGuildBadgeMessageParser()
        );
        builder.MapParser(
            MessageEvent.UpdateGuildColorsMessageEvent,
            new UpdateGuildColorsMessageParser()
        );
        builder.MapParser(
            MessageEvent.UpdateGuildIdentityMessageEvent,
            new UpdateGuildIdentityMessageParser()
        );
        builder.MapParser(
            MessageEvent.UpdateGuildSettingsMessageEvent,
            new UpdateGuildSettingsMessageParser()
        );

        builder.MapSerializer(
            typeof(GuildCreatedMessageComposer),
            new GuildCreatedMessageComposerSerializer(MessageComposer.GuildCreatedMessageComposer)
        );
        builder.MapSerializer(
            typeof(GuildCreationInfoMessageComposer),
            new GuildCreationInfoMessageComposerSerializer(
                MessageComposer.GuildCreationInfoMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(HabboGroupDetailsMessageComposer),
            new HabboGroupDetailsMessageComposerSerializer(
                MessageComposer.HabboGroupDetailsMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuildMembershipsMessageComposer),
            new GuildMembershipsMessageComposerSerializer(
                MessageComposer.GuildMembershipsMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuildMembersMessageComposer),
            new GuildMembersMessageComposerSerializer(MessageComposer.GuildMembersMessageComposer)
        );
        builder.MapSerializer(
            typeof(HabboGroupJoinFailedMessageComposer),
            new HabboGroupJoinFailedMessageComposerSerializer(
                MessageComposer.HabboGroupJoinFailedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuildEditInfoMessageComposer),
            new GuildEditInfoMessageComposerSerializer(MessageComposer.GuildEditInfoMessageComposer)
        );
        builder.MapSerializer(
            typeof(GuildEditorDataMessageComposer),
            new GuildEditorDataMessageComposerSerializer(
                MessageComposer.GuildEditorDataMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuildEditFailedMessageComposer),
            new GuildEditFailedMessageComposerSerializer(
                MessageComposer.GuildEditFailedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuildMembershipUpdatedMessageComposer),
            new GuildMembershipUpdatedMessageComposerSerializer(
                MessageComposer.GuildMembershipUpdatedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuildMembershipRejectedMessageComposer),
            new GuildMembershipRejectedMessageComposerSerializer(
                MessageComposer.GuildMembershipRejectedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuildMemberMgmtFailedMessageComposer),
            new GuildMemberMgmtFailedMessageComposerSerializer(
                MessageComposer.GuildMemberMgmtFailedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(HabboGroupDeactivatedMessageComposer),
            new HabboGroupDeactivatedMessageComposerSerializer(
                MessageComposer.HabboGroupDeactivatedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GroupMembershipRequestedMessageComposer),
            new GroupMembershipRequestedMessageComposerSerializer(
                MessageComposer.GroupMembershipRequestedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(HabboGroupBadgesMessageComposer),
            new HabboGroupBadgesMessageComposerSerializer(
                MessageComposer.HabboGroupBadgesMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuildMemberFurniCountInHQMessageComposer),
            new GuildMemberFurniCountInHQMessageComposerSerializer(
                MessageComposer.GuildMemberFurniCountInHQMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GroupDetailsChangedMessageComposer),
            new GroupDetailsChangedMessageComposerSerializer(
                MessageComposer.GroupDetailsChangedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(AccountSafetyLockStatusChangeMessageComposer),
            new AccountSafetyLockStatusChangeMessageComposerSerializer(
                MessageComposer.AccountSafetyLockStatusChangeMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ApproveNameMessageComposer),
            new ApproveNameMessageComposerSerializer(MessageComposer.ApproveNameMessageComposer)
        );
        builder.MapSerializer(
            typeof(ChangeEmailResultEventMessageComposer),
            new ChangeEmailResultEventMessageComposerSerializer(
                MessageComposer.ChangeEmailResultComposer
            )
        );
        builder.MapSerializer(
            typeof(EmailStatusResultEventMessageComposer),
            new EmailStatusResultEventMessageComposerSerializer(
                MessageComposer.EmailStatusResultComposer
            )
        );
        builder.MapSerializer(
            typeof(ExtendedProfileMessageComposer),
            new ExtendedProfileMessageComposerSerializer(
                MessageComposer.ExtendedProfileMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ExtendedProfileChangedMessageComposer),
            new ExtendedProfileChangedMessageComposerSerializer(
                MessageComposer.ExtendedProfileChangedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(BlockListMessageComposer),
            new BlockListMessageComposerSerializer(MessageComposer.BlockListMessageComposer)
        );
        builder.MapSerializer(
            typeof(BlockUserUpdateMessageComposer),
            new BlockUserUpdateMessageComposerSerializer(
                MessageComposer.BlockUserUpdateMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(IgnoreResultMessageComposer),
            new IgnoreResultMessageComposerSerializer(MessageComposer.IgnoreResultMessageComposer)
        );
        builder.MapSerializer(
            typeof(IgnoredUsersMessageComposer),
            new IgnoredUsersMessageComposerSerializer(MessageComposer.IgnoredUsersMessageComposer)
        );
        builder.MapSerializer(
            typeof(RelationshipStatusInfoEventMessageComposer),
            new RelationshipStatusInfoEventMessageComposerSerializer(
                MessageComposer.RelationshipStatusInfoComposer
            )
        );
        builder.MapSerializer(
            typeof(ScrSendKickbackInfoMessageComposer),
            new ScrSendKickbackInfoMessageComposerSerializer(
                MessageComposer.ScrSendKickbackInfoMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ScrSendUserInfoMessageComposer),
            new ScrSendUserInfoMessageSerializer(MessageComposer.ScrSendUserInfoComposer)
        );
        builder.MapSerializer(
            typeof(RespectNotificationMessageComposer),
            new RespectNotificationMessageComposerSerializer(
                MessageComposer.RespectNotificationMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(PetRespectNotificationEventMessageComposer),
            new PetRespectNotificationEventMessageComposerSerializer(
                MessageComposer.PetRespectNotificationComposer
            )
        );
    }
}

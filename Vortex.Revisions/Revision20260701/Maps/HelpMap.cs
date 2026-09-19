using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Help;
using Vortex.Revisions.Configuration;
using Vortex.Revisions.Revision20260701.Parsers.Help;
using Vortex.Revisions.Revision20260701.Serializers.Help;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class HelpMap : IRevisionMap
{
    private readonly ProtocolLimitsConfig _protocolLimits;

    public HelpMap(ProtocolLimitsConfig protocolLimits)
    {
        _protocolLimits = protocolLimits;
    }

    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(
            MessageEvent.CallForHelpFromForumMessageMessageEvent,
            new CallForHelpFromForumMessageMessageParser()
        );
        builder.MapParser(
            MessageEvent.CallForHelpFromForumThreadMessageEvent,
            new CallForHelpFromForumThreadMessageParser()
        );
        builder.MapParser(
            MessageEvent.CallForHelpFromIMMessageEvent,
            new CallForHelpFromIMMessageParser()
        );
        builder.MapParser(
            MessageEvent.CallForHelpFromPhotoMessageEvent,
            new CallForHelpFromPhotoMessageParser()
        );
        builder.MapParser(
            MessageEvent.CallForHelpFromSelfieMessageEvent,
            new CallForHelpFromSelfieMessageParser()
        );
        builder.MapParser(MessageEvent.CallForHelpMessageEvent, new CallForHelpMessageParser());
        builder.MapParser(
            MessageEvent.ChatReviewGuideDecidesOnOfferMessageEvent,
            new ChatReviewGuideDecidesOnOfferMessageParser()
        );
        builder.MapParser(
            MessageEvent.ChatReviewGuideDetachedMessageEvent,
            new ChatReviewGuideDetachedMessageParser()
        );
        builder.MapParser(
            MessageEvent.ChatReviewGuideVoteMessageEvent,
            new ChatReviewGuideVoteMessageParser()
        );
        builder.MapParser(
            MessageEvent.ChatReviewSessionCreateMessageEvent,
            new ChatReviewSessionCreateMessageParser()
        );
        builder.MapParser(
            MessageEvent.DeletePendingCallsForHelpMessageEvent,
            new DeletePendingCallsForHelpMessageParser()
        );
        builder.MapParser(MessageEvent.GetCfhStatusMessageEvent, new GetCfhStatusMessageParser());
        builder.MapParser(
            MessageEvent.GetGuideReportingStatusMessageEvent,
            new GetGuideReportingStatusMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetPendingCallsForHelpMessageEvent,
            new GetPendingCallsForHelpMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetMyCfhReportStatusMessageEvent,
            new GetMyCfhReportStatusMessageParser()
        );
        builder.MapParser(MessageEvent.AppealCfhMessageEvent, new AppealCfhMessageParser());
        builder.MapParser(MessageEvent.GetQuizQuestionsEvent, new GetQuizQuestionsMessageParser());
        builder.MapParser(
            MessageEvent.GuideSessionCreateMessageEvent,
            new GuideSessionCreateMessageParser()
        );
        builder.MapParser(
            MessageEvent.GuideSessionFeedbackMessageEvent,
            new GuideSessionFeedbackMessageParser()
        );
        builder.MapParser(
            MessageEvent.GuideSessionGetRequesterRoomMessageEvent,
            new GuideSessionGetRequesterRoomMessageParser()
        );
        builder.MapParser(
            MessageEvent.GuideSessionGuideDecidesMessageEvent,
            new GuideSessionGuideDecidesMessageParser()
        );
        builder.MapParser(
            MessageEvent.GuideSessionInviteRequesterMessageEvent,
            new GuideSessionInviteRequesterMessageParser()
        );
        builder.MapParser(
            MessageEvent.GuideSessionIsTypingMessageEvent,
            new GuideSessionIsTypingMessageParser()
        );
        builder.MapParser(
            MessageEvent.GuideSessionMessageMessageEvent,
            new GuideSessionMessageMessageParser()
        );
        builder.MapParser(
            MessageEvent.GuideSessionOnDutyUpdateMessageEvent,
            new GuideSessionOnDutyUpdateMessageParser()
        );
        builder.MapParser(
            MessageEvent.GuideSessionRequesterCancelsMessageEvent,
            new GuideSessionRequesterCancelsMessageParser()
        );
        builder.MapParser(
            MessageEvent.GuideSessionResolvedMessageEvent,
            new GuideSessionResolvedMessageParser()
        );
        builder.MapParser(
            MessageEvent.PostQuizAnswersEvent,
            new PostQuizAnswersMessageParser(_protocolLimits.MaxQuizAnswers)
        );

        builder.MapSerializer(
            typeof(GuideReportingStatusMessageComposer),
            new GuideReportingStatusMessageComposerSerializer(
                MessageComposer.GuideReportingStatusMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(QuizDataMessageComposer),
            new QuizDataMessageComposerSerializer(MessageComposer.QuizDataMessageComposer)
        );
        builder.MapSerializer(
            typeof(QuizResultsMessageComposer),
            new QuizResultsMessageComposerSerializer(MessageComposer.QuizResultsMessageComposer)
        );

        builder.MapSerializer(
            typeof(CallForHelpReplyMessageComposer),
            new CallForHelpReplyMessageComposerSerializer(
                MessageComposer.CallForHelpReplyMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(IssueCloseNotificationMessageComposer),
            new IssueCloseNotificationMessageComposerSerializer(
                MessageComposer.IssueCloseNotificationMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuideSessionAttachedMessageComposer),
            new GuideSessionAttachedMessageComposerSerializer(
                MessageComposer.GuideSessionAttachedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuideSessionStartedMessageComposer),
            new GuideSessionStartedMessageComposerSerializer(
                MessageComposer.GuideSessionStartedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuideSessionErrorMessageComposer),
            new GuideSessionErrorMessageComposerSerializer(
                MessageComposer.GuideSessionErrorMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuideSessionMessageMessageComposer),
            new GuideSessionMessageMessageComposerSerializer(
                MessageComposer.GuideSessionMessageMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuideSessionPartnerIsTypingMessageComposer),
            new GuideSessionPartnerIsTypingMessageComposerSerializer(
                MessageComposer.GuideSessionPartnerIsTypingMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuideSessionEndedMessageComposer),
            new GuideSessionEndedMessageComposerSerializer(
                MessageComposer.GuideSessionEndedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuideSessionRequesterRoomMessageComposer),
            new GuideSessionRequesterRoomMessageComposerSerializer(
                MessageComposer.GuideSessionRequesterRoomMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuideSessionInvitedToGuideRoomMessageComposer),
            new GuideSessionInvitedToGuideRoomMessageComposerSerializer(
                MessageComposer.GuideSessionInvitedToGuideRoomMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ChatReviewSessionOfferedToGuideMessageComposer),
            new ChatReviewSessionOfferedToGuideMessageComposerSerializer(
                MessageComposer.ChatReviewSessionOfferedToGuideMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ChatReviewSessionStartedMessageComposer),
            new ChatReviewSessionStartedMessageComposerSerializer(
                MessageComposer.ChatReviewSessionStartedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ChatReviewSessionVotingStatusMessageComposer),
            new ChatReviewSessionVotingStatusMessageComposerSerializer(
                MessageComposer.ChatReviewSessionVotingStatusMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(ChatReviewSessionResultsMessageComposer),
            new ChatReviewSessionResultsMessageComposerSerializer(
                MessageComposer.ChatReviewSessionResultsMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(GuideOnDutyStatusMessageComposer),
            new GuideOnDutyStatusMessageComposerSerializer(
                MessageComposer.GuideOnDutyStatusMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(CallForHelpPendingCallsMessageComposer),
            new CallForHelpPendingCallsMessageComposerSerializer(
                MessageComposer.CallForHelpPendingCallsMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(CallForHelpPendingCallsDeletedMessageComposer),
            new CallForHelpPendingCallsDeletedMessageComposerSerializer(
                MessageComposer.CallForHelpPendingCallsDeletedMessageComposer
            )
        );
    }
}

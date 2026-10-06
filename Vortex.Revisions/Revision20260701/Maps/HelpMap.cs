using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Help;
using Vortex.Revisions.Revision20260701.Parsers.Help;
using Vortex.Revisions.Revision20260701.Serializers.Help;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class HelpMap : IRevisionMap
{
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
        builder.MapParser(MessageEvent.CallForHelpMessageEvent, new CallForHelpMessageParser());
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

        builder.MapSerializer(
            typeof(GuideReportingStatusMessageComposer),
            new GuideReportingStatusMessageComposerSerializer(
                MessageComposer.GuideReportingStatusMessageComposer
            )
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

using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Quest;
using Vortex.Revisions.Revision20260701.Parsers.Quest;
using Vortex.Revisions.Revision20260701.Serializers.Quest;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class QuestMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(MessageEvent.AcceptQuestMessageEvent, new AcceptQuestMessageParser());
        builder.MapParser(MessageEvent.ActivateQuestMessageEvent, new ActivateQuestMessageParser());
        builder.MapParser(MessageEvent.CancelQuestMessageEvent, new CancelQuestMessageParser());
        builder.MapParser(
            MessageEvent.FriendRequestQuestCompleteMessageEvent,
            new FriendRequestQuestCompleteMessageParser()
        );
        builder.MapParser(MessageEvent.GetDailyTasksEvent, new GetDailyTasksMessageParser());
        builder.MapParser(MessageEvent.ClaimDailyTaskEvent, new ClaimDailyTaskMessageParser());
        builder.MapParser(MessageEvent.GetDailyQuestMessageEvent, new GetDailyQuestMessageParser());
        builder.MapParser(MessageEvent.GetQuestsMessageEvent, new GetQuestsMessageParser());
        builder.MapParser(
            MessageEvent.GetSeasonalQuestsOnlyMessageEvent,
            new GetSeasonalQuestsOnlyMessageParser()
        );
        builder.MapParser(
            MessageEvent.OpenQuestTrackerMessageEvent,
            new OpenQuestTrackerMessageParser()
        );
        builder.MapParser(MessageEvent.RejectQuestMessageEvent, new RejectQuestMessageParser());
        builder.MapParser(MessageEvent.StartCampaignMessageEvent, new StartCampaignMessageParser());

        builder.MapSerializer(
            typeof(QuestsMessageComposer),
            new QuestsMessageComposerSerializer(MessageComposer.QuestsMessageComposer)
        );
        builder.MapSerializer(
            typeof(QuestMessageComposer),
            new QuestMessageComposerSerializer(MessageComposer.QuestMessageComposer)
        );
        builder.MapSerializer(
            typeof(QuestDailyMessageComposer),
            new QuestDailyMessageComposerSerializer(MessageComposer.QuestDailyMessageComposer)
        );
        builder.MapSerializer(
            typeof(SeasonalQuestsMessageComposer),
            new SeasonalQuestsMessageComposerSerializer(
                MessageComposer.SeasonalQuestsMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(QuestCompletedMessageComposer),
            new QuestCompletedMessageComposerSerializer(
                MessageComposer.QuestCompletedMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(QuestCancelledMessageComposer),
            new QuestCancelledMessageComposerSerializer(
                MessageComposer.QuestCancelledMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(DailyTasksActiveListMessageComposer),
            new DailyTasksActiveListMessageComposerSerializer(
                MessageComposer.DailyTasksActiveListMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(DailyTasksTaskUpdateMessageComposer),
            new DailyTasksTaskUpdateMessageComposerSerializer(
                MessageComposer.DailyTasksTaskUpdateMessageComposer
            )
        );
    }
}

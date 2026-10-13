using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Rpg.Quests;
using Vortex.Revisions.Revision20260701.Parsers.Rpg.Quests;
using Vortex.Revisions.Revision20260701.Serializers.Rpg.Quests;

namespace Vortex.Revisions.Revision20260701.Maps;

/// <summary>NPCs, dialogue and quests (spec M2a §3.1–3.2).</summary>
internal sealed class RpgQuestMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(MessageEvent.RpgNpcTalkMessageEvent, new RpgNpcTalkMessageParser());
        builder.MapParser(
            MessageEvent.RpgDialogueChooseMessageEvent,
            new RpgDialogueChooseMessageParser()
        );
        builder.MapParser(
            MessageEvent.RpgQuestAbandonMessageEvent,
            new RpgQuestAbandonMessageParser()
        );
        builder.MapSerializer(
            typeof(RpgNpcsMessageComposer),
            new RpgNpcsMessageComposerSerializer(MessageComposer.RpgNpcsMessageComposer)
        );
        builder.MapSerializer(
            typeof(RpgDialogueMessageComposer),
            new RpgDialogueMessageComposerSerializer(MessageComposer.RpgDialogueMessageComposer)
        );
        builder.MapSerializer(
            typeof(RpgDialogueCloseMessageComposer),
            new RpgDialogueCloseMessageComposerSerializer(
                MessageComposer.RpgDialogueCloseMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RpgQuestLogMessageComposer),
            new RpgQuestLogMessageComposerSerializer(MessageComposer.RpgQuestLogMessageComposer)
        );
        builder.MapSerializer(
            typeof(RpgQuestResultMessageComposer),
            new RpgQuestResultMessageComposerSerializer(
                MessageComposer.RpgQuestResultMessageComposer
            )
        );
    }
}

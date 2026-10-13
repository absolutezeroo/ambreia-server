using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Quests;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Quests;

internal class RpgQuestLogMessageComposerSerializer(int header)
    : AbstractSerializer<RpgQuestLogMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgQuestLogMessageComposer message)
    {
        packet.WriteInteger(message.Quests.Length);

        foreach (RpgQuestLogEntry quest in message.Quests)
        {
            packet
                .WriteString(quest.QuestId)
                .WriteString(quest.State)
                .WriteInteger(quest.Objectives.Length);

            foreach (RpgQuestObjectiveLine line in quest.Objectives)
            {
                packet
                    .WriteString(line.Kind)
                    .WriteInteger(line.Current)
                    .WriteInteger(line.Required);
            }
        }
    }
}

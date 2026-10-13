using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Quests;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Quests;

internal class RpgQuestResultMessageComposerSerializer(int header)
    : AbstractSerializer<RpgQuestResultMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        RpgQuestResultMessageComposer message
    ) => packet.WriteString(message.Action).WriteString(message.Code).WriteString(message.QuestId);
}

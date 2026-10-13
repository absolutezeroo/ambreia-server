using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Quests;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Quests;

internal class RpgDialogueMessageComposerSerializer(int header)
    : AbstractSerializer<RpgDialogueMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgDialogueMessageComposer message)
    {
        packet
            .WriteInteger(message.NpcObjectId)
            .WriteString(message.Speaker)
            .WriteString(message.Text)
            .WriteInteger(message.Choices.Length);

        foreach (RpgDialogueChoice choice in message.Choices)
        {
            packet.WriteString(choice.Text).WriteString(choice.Kind);
        }
    }
}

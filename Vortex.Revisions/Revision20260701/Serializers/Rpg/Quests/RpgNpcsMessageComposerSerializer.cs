using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Quests;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Quests;

internal class RpgNpcsMessageComposerSerializer(int header)
    : AbstractSerializer<RpgNpcsMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgNpcsMessageComposer message)
    {
        packet.WriteInteger(message.Npcs.Length);

        foreach (RpgNpcMarker npc in message.Npcs)
        {
            packet.WriteInteger(npc.ObjectId).WriteString(npc.Marker);
        }
    }
}

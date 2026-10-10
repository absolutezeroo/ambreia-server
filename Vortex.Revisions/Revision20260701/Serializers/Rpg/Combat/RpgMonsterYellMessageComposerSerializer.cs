using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Combat;

internal class RpgMonsterYellMessageComposerSerializer(int header)
    : AbstractSerializer<RpgMonsterYellMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgMonsterYellMessageComposer message)
    {
        packet.WriteInteger(message.ObjectId).WriteString(message.TextKey);
    }
}

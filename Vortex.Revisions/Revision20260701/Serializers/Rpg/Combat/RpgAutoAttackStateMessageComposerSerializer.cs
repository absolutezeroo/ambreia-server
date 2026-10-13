using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Combat;

internal class RpgAutoAttackStateMessageComposerSerializer(int header) : AbstractSerializer<RpgAutoAttackStateMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgAutoAttackStateMessageComposer message)
    {
        packet.WriteBoolean(message.Active);
    }
}

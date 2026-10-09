using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Combat;

internal class RpgDeathMessageComposerSerializer(int header)
    : AbstractSerializer<RpgDeathMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgDeathMessageComposer message)
    {
        packet.WriteInteger(message.RespawnSeconds);
    }
}

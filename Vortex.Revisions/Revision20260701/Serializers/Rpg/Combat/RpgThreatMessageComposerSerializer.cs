using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Combat;

internal class RpgThreatMessageComposerSerializer(int header) : AbstractSerializer<RpgThreatMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgThreatMessageComposer message)
    {
        packet
            .WriteInteger(message.MonsterObjectId)
            .WriteInteger(message.Percent)
            .WriteBoolean(message.HasAggro);
    }
}

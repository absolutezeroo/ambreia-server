using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Combat;

internal class RpgCombatResultMessageComposerSerializer(int header)
    : AbstractSerializer<RpgCombatResultMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgCombatResultMessageComposer message)
    {
        packet.WriteString(message.Code);
    }
}

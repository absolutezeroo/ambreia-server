using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Combat;

internal class RpgSelfVitalsMessageComposerSerializer(int header)
    : AbstractSerializer<RpgSelfVitalsMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgSelfVitalsMessageComposer message)
    {
        packet
            .WriteInteger(message.Health)
            .WriteInteger(message.MaxHealth)
            .WriteInteger(message.Mana)
            .WriteInteger(message.MaxMana)
            .WriteBoolean(message.InCombat);
    }
}

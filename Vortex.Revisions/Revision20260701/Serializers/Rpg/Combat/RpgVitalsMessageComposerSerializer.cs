using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Combat;

internal class RpgVitalsMessageComposerSerializer(int header)
    : AbstractSerializer<RpgVitalsMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgVitalsMessageComposer message)
    {
        packet.WriteInteger(message.Combatants.Length);

        foreach (RpgCombatantVitals c in message.Combatants)
        {
            packet.WriteInteger(c.ObjectId).WriteInteger(c.Health).WriteInteger(c.MaxHealth);
        }
    }
}

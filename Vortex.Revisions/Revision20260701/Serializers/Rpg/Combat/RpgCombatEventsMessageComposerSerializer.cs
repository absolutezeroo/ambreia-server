using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Combat;

internal class RpgCombatEventsMessageComposerSerializer(int header)
    : AbstractSerializer<RpgCombatEventsMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgCombatEventsMessageComposer message)
    {
        packet.WriteInteger(message.Hits.Length);

        foreach (RpgCombatHit hit in message.Hits)
        {
            packet
                .WriteInteger(hit.AttackerId)
                .WriteInteger(hit.TargetId)
                .WriteInteger(hit.Amount)
                .WriteString(hit.Outcome)
                .WriteInteger(hit.Health)
                .WriteInteger(hit.MaxHealth)
                .WriteBoolean(hit.Killed);
        }
    }
}

using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Skills;

internal class RpgStatusesMessageComposerSerializer(int header)
    : AbstractSerializer<RpgStatusesMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgStatusesMessageComposer message)
    {
        packet.WriteInteger(message.Combatants.Length);

        foreach (RpgCombatantStatuses combatant in message.Combatants)
        {
            packet.WriteInteger(combatant.ObjectId).WriteInteger(combatant.Statuses.Length);

            foreach (RpgStatusEntry status in combatant.Statuses)
            {
                packet
                    .WriteString(status.StatusId)
                    .WriteInteger(status.RemainingMs)
                    .WriteInteger(status.Stacks);
            }
        }
    }
}

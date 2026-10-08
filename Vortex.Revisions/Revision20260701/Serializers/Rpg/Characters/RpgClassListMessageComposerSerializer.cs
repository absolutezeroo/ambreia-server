using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Characters;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Characters;

internal class RpgClassListMessageComposerSerializer(int header)
    : AbstractSerializer<RpgClassListMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgClassListMessageComposer message)
    {
        packet.WriteInteger(message.Classes.Length);

        foreach (RpgClassEntry entry in message.Classes)
        {
            packet
                .WriteString(entry.Id)
                .WriteString(entry.Role)
                .WriteInteger(entry.Range)
                .WriteString(entry.PrimaryStat)
                .WriteBoolean(entry.Starter)
                .WriteInteger(entry.Specializations.Length);

            foreach (string specialization in entry.Specializations)
            {
                packet.WriteString(specialization);
            }
        }
    }
}

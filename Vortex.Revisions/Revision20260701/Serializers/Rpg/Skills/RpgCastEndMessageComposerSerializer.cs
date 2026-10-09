using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Skills;

internal class RpgCastEndMessageComposerSerializer(int header)
    : AbstractSerializer<RpgCastEndMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgCastEndMessageComposer message)
    {
        packet.WriteInteger(message.Ends.Length);

        foreach (RpgCastEnd end in message.Ends)
        {
            packet
                .WriteInteger(end.CasterObjectId)
                .WriteString(end.SkillId)
                .WriteString(end.Reason);
        }
    }
}

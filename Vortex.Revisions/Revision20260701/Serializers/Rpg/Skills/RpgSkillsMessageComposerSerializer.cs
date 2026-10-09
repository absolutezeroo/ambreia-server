using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Skills;

internal class RpgSkillsMessageComposerSerializer(int header)
    : AbstractSerializer<RpgSkillsMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgSkillsMessageComposer message)
    {
        packet.WriteInteger(message.Known.Length);

        foreach (string skill in message.Known)
        {
            packet.WriteString(skill);
        }

        packet.WriteInteger(message.Bar.Length);

        foreach (string action in message.Bar)
        {
            packet.WriteString(action);
        }
    }
}

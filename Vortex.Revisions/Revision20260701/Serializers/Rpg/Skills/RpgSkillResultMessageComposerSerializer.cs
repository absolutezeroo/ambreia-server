using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Skills;

internal class RpgSkillResultMessageComposerSerializer(int header)
    : AbstractSerializer<RpgSkillResultMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgSkillResultMessageComposer message)
    {
        packet.WriteString(message.SkillId).WriteString(message.Code);
    }
}

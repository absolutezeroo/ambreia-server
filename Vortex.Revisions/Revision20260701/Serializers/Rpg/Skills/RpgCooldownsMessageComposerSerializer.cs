using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Skills;

internal class RpgCooldownsMessageComposerSerializer(int header)
    : AbstractSerializer<RpgCooldownsMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgCooldownsMessageComposer message)
    {
        packet.WriteInteger(message.GlobalRemainingMs).WriteInteger(message.Skills.Length);

        foreach (RpgCooldown cooldown in message.Skills)
        {
            packet.WriteString(cooldown.SkillId).WriteInteger(cooldown.RemainingMs);
        }
    }
}

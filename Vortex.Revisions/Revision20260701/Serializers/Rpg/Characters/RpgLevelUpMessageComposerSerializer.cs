using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Characters;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Characters;

internal class RpgLevelUpMessageComposerSerializer(int header) : AbstractSerializer<RpgLevelUpMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgLevelUpMessageComposer message)
    {
        packet.WriteInteger(message.FromLevel).WriteInteger(message.ToLevel);
        RpgStatBlockWriter.Write(packet, message.StatsBefore);
        RpgStatBlockWriter.Write(packet, message.StatsAfter);
        packet.WriteInteger(message.SkillsLearned.Length);

        foreach (string skillId in message.SkillsLearned)
        {
            packet.WriteString(skillId);
        }
    }
}

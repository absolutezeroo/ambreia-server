using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Skills;

internal class RpgTelegraphsMessageComposerSerializer(int header)
    : AbstractSerializer<RpgTelegraphsMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgTelegraphsMessageComposer message)
    {
        packet.WriteInteger(message.Telegraphs.Length);

        foreach (RpgTelegraph telegraph in message.Telegraphs)
        {
            packet
                .WriteInteger(telegraph.CasterObjectId)
                .WriteString(telegraph.SkillId)
                .WriteString(telegraph.Shape)
                .WriteInteger(telegraph.DelayMs)
                .WriteInteger(telegraph.Cells.Length);

            foreach ((int x, int y) in telegraph.Cells)
            {
                packet.WriteInteger(x).WriteInteger(y);
            }
        }
    }
}

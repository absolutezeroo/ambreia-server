using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Skills;

internal class RpgCastStartMessageComposerSerializer(int header)
    : AbstractSerializer<RpgCastStartMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgCastStartMessageComposer message)
    {
        packet.WriteInteger(message.Casts.Length);

        foreach (RpgCastStart cast in message.Casts)
        {
            packet
                .WriteInteger(cast.CasterObjectId)
                .WriteString(cast.SkillId)
                .WriteInteger(cast.DurationMs)
                .WriteBoolean(cast.Channel)
                .WriteInteger(cast.TargetObjectId)
                .WriteInteger(cast.X)
                .WriteInteger(cast.Y);
        }
    }
}

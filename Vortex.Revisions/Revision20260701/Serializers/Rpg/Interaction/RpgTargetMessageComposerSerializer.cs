using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Interaction;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Interaction;

internal class RpgTargetMessageComposerSerializer(int header)
    : AbstractSerializer<RpgTargetMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgTargetMessageComposer message)
    {
        packet
            .WriteInteger(message.ObjectId)
            .WriteString(message.Kind)
            .WriteString(message.Name)
            .WriteInteger(message.Level)
            .WriteString(message.ClassId);
    }
}

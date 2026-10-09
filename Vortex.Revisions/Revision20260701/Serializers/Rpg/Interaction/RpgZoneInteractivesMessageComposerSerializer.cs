using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Interaction;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Interaction;

internal class RpgZoneInteractivesMessageComposerSerializer(int header)
    : AbstractSerializer<RpgZoneInteractivesMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        RpgZoneInteractivesMessageComposer message
    )
    {
        packet.WriteInteger(message.Objects.Length);

        foreach (RpgZoneInteractive item in message.Objects)
        {
            packet.WriteInteger(item.ObjectId).WriteString(item.Interaction);
        }
    }
}

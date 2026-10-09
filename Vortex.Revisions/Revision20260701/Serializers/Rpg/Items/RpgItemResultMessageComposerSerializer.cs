using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Items;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Items;

internal class RpgItemResultMessageComposerSerializer(int header)
    : AbstractSerializer<RpgItemResultMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgItemResultMessageComposer message)
    {
        packet.WriteInteger(message.Action).WriteString(message.Code);
    }
}

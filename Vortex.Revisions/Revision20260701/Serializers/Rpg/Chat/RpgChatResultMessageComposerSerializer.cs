using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Chat;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Chat;

internal class RpgChatResultMessageComposerSerializer(int header)
    : AbstractSerializer<RpgChatResultMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgChatResultMessageComposer message)
    {
        packet.WriteString(message.Channel).WriteString(message.Code);
    }
}

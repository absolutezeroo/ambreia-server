using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Chat;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Chat;

internal class RpgChatMessageComposerSerializer(int header)
    : AbstractSerializer<RpgChatMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgChatMessageComposer message)
    {
        packet
            .WriteString(message.Channel)
            .WriteInteger(message.SenderId)
            .WriteString(message.SenderName)
            .WriteString(message.Text)
            .WriteInteger(message.SentAt)
            .WriteString(message.Target);
    }
}

using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Emote;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Emote;

internal class RpgEmoteMessageComposerSerializer(int header)
    : AbstractSerializer<RpgEmoteMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgEmoteMessageComposer message)
    {
        packet.WriteInteger(message.ObjectId).WriteString(message.Key);
    }
}

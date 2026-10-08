using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Characters;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Characters;

internal class RpgCharacterResultMessageComposerSerializer(int header)
    : AbstractSerializer<RpgCharacterResultMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        RpgCharacterResultMessageComposer message
    )
    {
        packet
            .WriteInteger(message.Action)
            .WriteString(message.Code)
            .WriteInteger(message.PlayerId);
    }
}

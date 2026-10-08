using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Characters;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Characters;

internal class RpgCharacterProgressMessageComposerSerializer(int header)
    : AbstractSerializer<RpgCharacterProgressMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        RpgCharacterProgressMessageComposer message
    )
    {
        packet.WriteInteger(message.Level).WriteInteger(message.Xp).WriteInteger(message.XpToNext);
    }
}

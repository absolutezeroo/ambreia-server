using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Characters;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Characters;

internal class RpgCharacterStateMessageComposerSerializer(int header)
    : AbstractSerializer<RpgCharacterStateMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        RpgCharacterStateMessageComposer message
    )
    {
        packet
            .WriteInteger(message.PlayerId)
            .WriteString(message.ClassId)
            .WriteString(message.SpecializationId)
            .WriteInteger(message.Level)
            .WriteInteger(message.MaxLevel)
            .WriteInteger(message.Xp)
            .WriteInteger(message.XpToNext);
        RpgStatBlockWriter.Write(packet, message.Stats);
    }
}

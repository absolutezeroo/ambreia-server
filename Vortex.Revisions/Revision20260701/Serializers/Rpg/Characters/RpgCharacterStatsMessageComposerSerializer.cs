using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Characters;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Characters;

internal class RpgCharacterStatsMessageComposerSerializer(int header)
    : AbstractSerializer<RpgCharacterStatsMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        RpgCharacterStatsMessageComposer message
    ) => RpgStatBlockWriter.Write(packet, message.Stats);
}

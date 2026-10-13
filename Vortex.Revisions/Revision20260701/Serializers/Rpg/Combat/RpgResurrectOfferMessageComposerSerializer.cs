using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Combat;

internal class RpgResurrectOfferMessageComposerSerializer(int header) : AbstractSerializer<RpgResurrectOfferMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RpgResurrectOfferMessageComposer message)
    {
        packet
            .WriteInteger(message.CasterObjectId)
            .WriteInteger(message.HealthPercent)
            .WriteInteger(message.ExpiresMs);
    }
}

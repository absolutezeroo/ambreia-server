using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Inventory.Badges;

namespace Vortex.Revisions.Revision20260701.Serializers.Inventory.Badges;

internal class BadgeInfoMessageComposerSerializer(int header)
    : AbstractSerializer<BadgeInfoMessageComposer>(header)
{
    /// <summary>
    /// Four fields, in the order <c>unknowns/_SafePkg_3206/_SafeCls_4369.parse</c> reads them:
    /// badgeId, badgeCode, ownerCount, badgeRarityId.
    /// </summary>
    protected override void Serialize(IServerPacket packet, BadgeInfoMessageComposer message) =>
        packet
            .WriteInteger(message.BadgeId)
            .WriteString(message.BadgeCode)
            .WriteInteger(message.OwnerCount)
            .WriteInteger(message.BadgeRarityId);
}

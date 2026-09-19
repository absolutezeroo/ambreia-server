using Vortex.Primitives.Packets;
using Vortex.Primitives.Players.Snapshots;
using Vortex.Protocol.Messages.Outgoing.Inventory.Badges;

namespace Vortex.Revisions.Revision20260701.Serializers.Inventory.Badges;

internal class BadgesEventMessageComposerSerializer(int header)
    : AbstractSerializer<BadgesEventMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, BadgesEventMessageComposer message)
    {
        packet.WriteInteger(1); // totalFragments
        // fragmentNo is zero-based: a live capture of the official server sending a single
        // fragment carries 1/0, and every other fragmented message here writes 0 too. The client
        // short-circuits on totalFragments == 1 before it ever reads this, so the 1 was harmless
        // -- it only stops being harmless the day this message is actually split.
        packet.WriteInteger(0); // fragmentNo
        packet.WriteInteger(message.Badges.Length);

        // Four fields per badge, not two: WIN63's parser
        // (unknowns/_SafePkg_3206/_SafeCls_3564.as) reads slotId, badgeCode, ownerCount and
        // badgeRarityId, and builds a Badge from all four. Sending only the first two left every
        // badge with rarity 0, which is what the client's rarity overlay tests.
        foreach (PlayerBadgeSnapshot badge in message.Badges)
        {
            packet.WriteInteger(badge.SlotId);
            packet.WriteString(badge.BadgeCode);
            packet.WriteInteger(badge.OwnerCount);
            packet.WriteInteger(badge.BadgeRarityId);
        }
    }
}

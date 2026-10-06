using Vortex.Primitives.Groups.Snapshots;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Users;
using Vortex.Revisions.Revision20260701.Serializers.Users.Data;

namespace Vortex.Revisions.Revision20260701.Serializers.Users;

internal class ExtendedProfileMessageComposerSerializer(int header)
    : AbstractSerializer<ExtendedProfileMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, ExtendedProfileMessageComposer message)
    {
        packet
            .WriteInteger(message.UserId)
            .WriteString(message.UserName)
            .WriteString(message.Figure)
            .WriteString(message.Motto)
            .WriteString(message.CreationDate)
            .WriteInteger(message.AchievementScore)
            .WriteInteger(message.FriendCount)
            .WriteBoolean(message.IsFriend)
            .WriteBoolean(message.IsFriendRequestSent)
            .WriteByte((byte)message.OnlineStatus)
            .WriteInteger(message.Guilds.Count);

        foreach (GuildInfoSnapshot guild in message.Guilds)
        {
            GuildInfoSerializer.Serialize(packet, guild);
        }

        packet
            .WriteInteger(message.LastAccessSinceInSeconds)
            .WriteBoolean(message.OpenProfileWindow)
            .WriteBoolean(message.IsHidden)
            // accountLevel: frozen at 0 since account levels were removed (M0 bis). The client
            // still reads it; the field goes when the client drops it.
            .WriteInteger(0)
            .WriteInteger(message.IntegerField24)
            .WriteInteger(message.StarGemCount)
            .WriteBoolean(message.BooleanField26)
            .WriteBoolean(message.BooleanField27)
            // The last four reads of WIN63's parser
            // (unknowns/_SafePkg_1731/_SafeCls_2228.as), which this composer used to stop short
            // of. The client reads them unconditionally, so leaving them off truncated the packet
            // rather than merely hiding the badge panel.
            .WriteInteger(message.TotalBadges)
            .WriteInteger(message.AchievementLevel)
            .WriteInteger(message.BadgeRarityCounts.Count);

        foreach (BadgeRarityCount rarity in message.BadgeRarityCounts)
        {
            packet.WriteByte((byte)rarity.RarityId).WriteInteger(rarity.Count);
        }

        packet.WriteInteger(message.TotalBadgesRank);
    }
}

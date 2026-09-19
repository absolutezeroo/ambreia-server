using Vortex.Primitives.Badges.Snapshots;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.Revisions.Revision20260701.Serializers.Users;

internal class BadgeLeaderboardResultMessageComposerSerializer(int header)
    : AbstractSerializer<BadgeLeaderboardResultMessageComposer>(header)
{
    /// <summary>
    /// The order <c>unknowns/_SafePkg_1891/_SafeCls_3883.parse</c> reads: type, rarity, page, size,
    /// totalEntries, the counted rows, then a boolean that gates one trailing row.
    /// </summary>
    protected override void Serialize(
        IServerPacket packet,
        BadgeLeaderboardResultMessageComposer message
    )
    {
        BadgeLeaderboardPageSnapshot page = message.Page;

        packet
            .WriteInteger((int)page.Type)
            .WriteInteger(page.Rarity)
            .WriteInteger(page.ChunkIndex)
            .WriteInteger(page.ChunkSize)
            .WriteInteger(page.TotalEntries)
            .WriteInteger(page.Entries.Length);

        foreach (BadgeLeaderboardEntrySnapshot entry in page.Entries)
        {
            WriteEntry(packet, entry);
        }

        packet.WriteBoolean(page.OwnEntry is not null);

        if (page.OwnEntry is not null)
        {
            WriteEntry(packet, page.OwnEntry);
        }
    }

    /// <summary>Five fields per row, as <c>unknowns/_SafePkg_1731/_SafeCls_3225</c> reads them.</summary>
    private static void WriteEntry(IServerPacket packet, BadgeLeaderboardEntrySnapshot entry) =>
        packet
            .WriteInteger(entry.PlayerId)
            .WriteString(entry.Name)
            .WriteString(entry.Figure)
            .WriteInteger(entry.Rank)
            .WriteInteger(entry.Score);
}

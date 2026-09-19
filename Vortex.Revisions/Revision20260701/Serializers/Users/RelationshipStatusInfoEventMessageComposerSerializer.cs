using Vortex.Primitives.Packets;
using Vortex.Primitives.Snapshots.FriendList;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.Revisions.Revision20260701.Serializers.Users;

internal class RelationshipStatusInfoEventMessageComposerSerializer(int header)
    : AbstractSerializer<RelationshipStatusInfoEventMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        RelationshipStatusInfoEventMessageComposer message
    )
    {
        packet.WriteInteger(message.UserId);
        packet.WriteInteger(message.Relations.Count);

        foreach (RelationshipStatusEntrySnapshot rel in message.Relations)
        {
            // Five fields per entry, all read by one data class the scanner never opened:
            // WIN63 unknowns/_SafePkg_1731/_SafeCls_3266.as:22 reads relationshipStatusType,
            // friendCount, randomFriendId, randomFriendName, randomFriendFigure -- three ints then
            // two strings. A short for the type and no friend id left the client reading the rest
            // of the packet two bytes early and one field short per entry.
            packet
                .WriteInteger(rel.RelationType)
                .WriteInteger(rel.Count)
                .WriteInteger(rel.RandomFriendId)
                .WriteString(rel.Name)
                .WriteString(rel.Figure);
        }
    }
}

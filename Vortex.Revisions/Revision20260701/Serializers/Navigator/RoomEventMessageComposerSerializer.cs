using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Navigator;

namespace Vortex.Revisions.Revision20260701.Serializers.Navigator;

/// <summary>
/// Field order is the client's, read off its parser: RoomEventData's constructor consumes ten
/// values back to back with no bytes-available guard, so this writes all ten in that order.
/// </summary>
internal class RoomEventMessageComposerSerializer(int header)
    : AbstractSerializer<RoomEventMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RoomEventMessageComposer message)
    {
        packet
            .WriteInteger(message.AdvertisementId)
            .WriteInteger(message.OwnerPlayerId)
            .WriteString(message.OwnerName)
            .WriteInteger(message.RoomId)
            .WriteInteger(message.EventType)
            .WriteString(message.Name)
            .WriteString(message.Description ?? string.Empty)
            .WriteInteger(message.MinutesSinceCreation)
            .WriteInteger(message.MinutesRemaining)
            .WriteInteger(message.CategoryId);
    }
}

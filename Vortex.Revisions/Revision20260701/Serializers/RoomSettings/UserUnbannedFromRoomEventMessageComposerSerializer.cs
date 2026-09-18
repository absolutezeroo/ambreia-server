using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Roomsettings;

namespace Vortex.Revisions.Revision20260701.Serializers.RoomSettings;

internal class UserUnbannedFromRoomEventMessageComposerSerializer(int header)
    : AbstractSerializer<UserUnbannedFromRoomEventMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        UserUnbannedFromRoomEventMessageComposer message
    )
    {
        // Room first, then player: unknowns/_SafePkg_2452/_SafeCls_3788.parse reads roomId and
        // exposes userId second, and the handler passes them on in that order.
        packet.WriteInteger(message.RoomId).WriteInteger(message.PlayerId);
    }
}

using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Navigator;
using Vortex.Revisions.Revision20260701.Serializers.Navigator.Data;

namespace Vortex.Revisions.Revision20260701.Serializers.Navigator;

internal class GetGuestRoomResultMessageComposerSerializer(int header)
    : AbstractSerializer<GetGuestRoomResultMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        GetGuestRoomResultMessageComposer message
    )
    {
        packet.WriteBoolean(message.EnterRoom);

        RoomSettingsSerializer.Serialize(packet, message.RoomInfo);

        // The staff-pick flag is frozen at false: staff picks are gone but the client still reads it.
        packet
            .WriteBoolean(message.RoomForward)
            .WriteBoolean(false)
            .WriteBoolean(message.IsGroupMember)
            .WriteBoolean(message.AllInRoomMuted);

        ModSettingsSnapshotSerializer.Serialize(packet, message.RoomInfo.ModSettings);

        // One int, not the five the older revisions wrote here: this client reads
        // `fromFloodSensitivity(readInteger())` (_SafePkg_2064/_SafeCls_2063.as:50) — chat mode,
        // bubble width and scroll speed moved to the *account* preferences packet, and the four
        // surplus ints used to push `openingConnection` onto the wrong bytes.
        packet
            .WriteBoolean(message.CanMute)
            .WriteInteger((int)message.RoomInfo.ChatSettings.FloodSensitivity)
            .WriteBoolean(message.OpeningConnection);
    }
}

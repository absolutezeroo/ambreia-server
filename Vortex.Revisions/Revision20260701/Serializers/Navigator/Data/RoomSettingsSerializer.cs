using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Revisions.Revision20260701.Serializers.Navigator.Data;

internal class RoomSettingsSerializer
{
    public static void Serialize(IServerPacket packet, RoomInfoSnapshot message)
    {
        packet
            .WriteInteger(message.RoomId)
            .WriteString(message.Name)
            .WriteInteger(message.OwnerId)
            .WriteString(message.OwnerName)
            .WriteInteger((int)message.DoorMode)
            .WriteInteger(message.Population)
            .WriteInteger(message.PlayersMax)
            .WriteString(message.Description)
            .WriteInteger((int)message.TradeType)
            .WriteInteger(message.Score)
            .WriteInteger(message.Ranking)
            .WriteInteger(message.CategoryId)
            .WriteInteger(message.Tags.Length);

        foreach (string tag in message.Tags)
        {
            packet.WriteString(tag);
        }

        RoomBitmaskFlags bitmask = RoomBitmaskFlags.None;

        // The client draws an "owner" line from this bit alone and does not check the name it was
        // given, so announcing an owner we cannot name renders an empty label.
        if (!string.IsNullOrEmpty(message.OwnerName))
        {
            bitmask |= RoomBitmaskFlags.ShowOwner;
        }

        if (message.AllowPets)
        {
            bitmask |= RoomBitmaskFlags.AllowPets;
        }

        if (message.GroupId.HasValue && message.GroupName != null && message.GroupBadge != null)
        {
            bitmask |= RoomBitmaskFlags.GroupData;
        }

        // The RoomAd bit (4) and DisplayRoomAd (32) stay clear on purpose: room advertisements are
        // gone, so the ad name/description/countdown trio behind bit 4 is never written, and bit 32
        // would make every entering client fetch an interstitial this hotel does not serve.

        packet.WriteInteger((int)bitmask);

        if (bitmask.HasFlag(RoomBitmaskFlags.Thumbnail))
        {
            packet.WriteString(string.Empty);
        }

        if (bitmask.HasFlag(RoomBitmaskFlags.GroupData))
        {
            packet
                .WriteInteger(message.GroupId!.Value)
                .WriteString(message.GroupName!)
                .WriteString(message.GroupBadge!);
        }
    }
}

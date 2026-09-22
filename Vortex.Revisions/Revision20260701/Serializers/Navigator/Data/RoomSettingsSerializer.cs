using System;
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

        if (!string.IsNullOrEmpty(message.AdName))
        {
            bitmask |= RoomBitmaskFlags.RoomAd;
        }

        // DisplayRoomAd (bit 32) stays clear on purpose. It is not "this room is advertised" -- the
        // client answers it with requestRoomEnterAd(), a call out to Habbo's web ad slot, so setting
        // it asks every entering player's client to fetch an interstitial this hotel does not serve.

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

        if (bitmask.HasFlag(RoomBitmaskFlags.RoomAd))
        {
            packet
                .WriteString(message.AdName)
                .WriteString(message.AdDescription)
                .WriteInteger(MinutesUntil(message.AdExpiresAt));
        }
    }

    /// <summary>Minutes the client should count down from, floored at 0. It renders the value as a
    /// friendly duration and treats a non-positive one as "no ad running", so an expired snapshot
    /// degrades to silence rather than to a negative timer.</summary>
    private static int MinutesUntil(DateTime? expiresAt)
    {
        if (expiresAt is null)
        {
            return 0;
        }

        double minutes = (expiresAt.Value - DateTime.UtcNow).TotalMinutes;

        return minutes <= 0 ? 0 : (int)Math.Ceiling(minutes);
    }
}

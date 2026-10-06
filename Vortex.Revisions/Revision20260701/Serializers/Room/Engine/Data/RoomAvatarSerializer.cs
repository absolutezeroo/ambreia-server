using System.Globalization;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Snapshots.Avatars;

namespace Vortex.Revisions.Revision20260701.Serializers.Room.Engine.Data;

internal class RoomAvatarSerializer
{
    public static void Serialize(IServerPacket packet, RoomAvatarSnapshot item)
    {
        packet
            .WriteInteger(item.WebId)
            .WriteString(item.Name)
            .WriteString(item.Motto)
            .WriteString(item.Figure)
            .WriteInteger(item.ObjectId)
            .WriteInteger(item.X)
            .WriteInteger(item.Y)
            .WriteString(item.Z.ToString())
            .WriteInteger((int)item.BodyRotation)
            .WriteInteger((int)item.AvatarType);

        if (item is RoomPlayerAvatarSnapshot player)
        {
            SerializePlayerAvatar(packet, player);
        }
        else if (item is RoomPetAvatarSnapshot pet)
        {
            SerializePetAvatar(packet, pet);
        }
        else if (item is RoomBotAvatarSnapshot bot)
        {
            SerializeBotAvatar(packet, bot);
        }
    }

    public static void SerializePlayerAvatar(
        IServerPacket packet,
        RoomPlayerAvatarSnapshot snapshot
    )
    {
        packet
            .WriteString(AvatarGenderTypeExtensions.ToLegacyString(snapshot.Gender))
            .WriteInteger(snapshot.GroupId)
            .WriteInteger(snapshot.GroupStatus)
            .WriteString(snapshot.GroupName)
            .WriteString(snapshot.SwimFigure)
            .WriteInteger(snapshot.ActivityPoints)
            .WriteBoolean(snapshot.IsModerator)
            .WriteInteger(snapshot.BadgesRank);
    }

    public static void SerializePetAvatar(IServerPacket packet, RoomPetAvatarSnapshot snapshot)
    {
        packet
            .WriteInteger(snapshot.SubType)
            .WriteInteger(snapshot.OwnerId)
            .WriteString(snapshot.OwnerName)
            // Rarity, then after the saddle and rider the breed, harvest and revive flags and the
            // breeding permission: breeding and monsterplants were removed in M0, and the client
            // still reads all five. -1 is the rarity the live hotel sends for an animal.
            .WriteInteger(-1)
            .WriteBoolean(snapshot.HasSaddle)
            .WriteBoolean(snapshot.IsRiding)
            .WriteBoolean(false)
            .WriteBoolean(false)
            .WriteBoolean(false)
            .WriteBoolean(false)
            .WriteInteger(snapshot.PetLevel)
            .WriteString(snapshot.PetPosture);
    }

    public static void SerializeBotAvatar(IServerPacket packet, RoomBotAvatarSnapshot snapshot)
    {
        packet
            .WriteString(AvatarGenderTypeExtensions.ToLegacyString(snapshot.Gender))
            .WriteInteger(snapshot.OwnerId)
            .WriteString(snapshot.OwnerName)
            .WriteInteger(snapshot.SkillIds.Length);

        // Shorts, not ints. The client only enters this loop when the count is above zero, but it
        // reads the count either way, so it is always written.
        foreach (short skillId in snapshot.SkillIds)
        {
            packet.WriteShort(skillId);
        }
    }
}

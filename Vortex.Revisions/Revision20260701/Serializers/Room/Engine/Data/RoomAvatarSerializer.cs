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
            .WriteInteger(snapshot.ActivityPoints)
            .WriteBoolean(snapshot.IsModerator);
    }

    public static void SerializePetAvatar(IServerPacket packet, RoomPetAvatarSnapshot snapshot)
    {
        packet
            .WriteInteger(snapshot.SubType)
            .WriteInteger(snapshot.OwnerId)
            .WriteString(snapshot.OwnerName)
            .WriteBoolean(snapshot.HasSaddle)
            .WriteBoolean(snapshot.IsRiding)
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

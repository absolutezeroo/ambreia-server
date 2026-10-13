using System.Text;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Avatars;

namespace Vortex.Rooms.Object.Avatars.Npc;

/// <summary>
/// A zone's NPC (spec M2a §3.3), shown as a Habbo bot: its web id its object id negated (it is nobody),
/// its name the editor's, its figure the editor's look. It has no object logic and never walks.
/// </summary>
public sealed class RoomNpcAvatar : RoomAvatar<IRoomNpc, IRoomNpcLogic, IRoomNpcContext>, IRoomNpc
{
    /// <summary>The look of an NPC the editor gave none.</summary>
    public const string DefaultFigure = "hd-180-1.ch-210-66.lg-270-82.sh-290-81";

    public override RoomObjectType AvatarType { get; } = RoomObjectType.Bot;

    public required string NpcId { get; init; }

    public string? Dialogue { get; init; }

    /// <summary>The editor's name, or its id: the base's Name, written once.</summary>
    public required string NpcName
    {
        init => Name = value;
    }

    public required string Look
    {
        init => Figure = value;
    }

    protected override RoomBotAvatarSnapshot BuildSnapshot()
    {
        StringBuilder statusString = new("/");

        foreach ((AvatarStatusType type, string value) in Statuses)
        {
            statusString.Append($"{type.ToLegacyString()} {value}/");
        }

        return new RoomBotAvatarSnapshot
        {
            AvatarType = AvatarType,
            WebId = -ObjectId.Value,
            Name = Name,
            Motto = string.Empty,
            Figure = Figure,
            ObjectId = ObjectId,
            X = X,
            Y = Y,
            Z = Z,
            BodyRotation = Rotation,
            HeadRotation = HeadRotation,
            Status = statusString.ToString(),
            Gender = AvatarGenderType.Male,
            OwnerId = 0,
            OwnerName = string.Empty,
        };
    }
}

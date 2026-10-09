using System.Text;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Avatars;

namespace Vortex.Rooms.Object.Avatars.Monster;

/// <summary>
/// A monster in a zone (spec M1a §9.1), shown as a Habbo pet: its web id is its object id negated (it
/// is nobody's), its name its content id for the client to translate, its level the pet level. It
/// has no object logic: the zone simulation drives it.
/// </summary>
public sealed class RoomMonsterAvatar
    : RoomAvatar<IRoomMonster, IRoomMonsterLogic, IRoomMonsterContext>,
        IRoomMonster
{
    public override RoomObjectType AvatarType { get; } = RoomObjectType.Pet;

    public required string DefinitionId { get; init; }

    public required int Level { get; init; }

    /// <summary>The pet figure "&lt;type&gt; &lt;race&gt; &lt;colour&gt;".</summary>
    public required string PetFigure
    {
        init => Figure = value;
    }

    protected override RoomPetAvatarSnapshot BuildSnapshot()
    {
        StringBuilder statusString = new("/");

        foreach ((AvatarStatusType type, string value) in Statuses)
        {
            statusString.Append($"{type.ToLegacyString()} {value}/");
        }

        return new RoomPetAvatarSnapshot
        {
            AvatarType = AvatarType,
            WebId = -ObjectId.Value,
            Name = DefinitionId,
            Motto = string.Empty,
            Figure = Figure,
            ObjectId = ObjectId,
            X = X,
            Y = Y,
            Z = Z,
            BodyRotation = Rotation,
            HeadRotation = HeadRotation,
            Status = statusString.ToString(),
            SubType = 0,
            OwnerId = 0,
            OwnerName = string.Empty,
            HasSaddle = false,
            IsRiding = false,
            PetLevel = Level,
            PetPosture = string.Empty,
        };
    }
}

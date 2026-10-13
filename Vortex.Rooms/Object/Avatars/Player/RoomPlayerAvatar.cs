using System.Text;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Avatars;

namespace Vortex.Rooms.Object.Avatars.Player;

public sealed class RoomPlayerAvatar
    : RoomAvatar<IRoomPlayer, IRoomPlayerLogic, IRoomPlayerContext>,
        IRoomPlayer
{
    /// <summary>
    /// The achievement score the avatar block reports. This was <c>init</c>-only and nothing ever
    /// initialised it — <c>RoomAvatarProvider.CreateAvatarFromPlayerSnapshot</c> builds the avatar
    /// with only ObjectId/PlayerId and then calls <see cref="UpdateWithPlayer"/> — so every avatar
    /// in every room serialised a hard 0 and the client's infostand showed 0 for a player whose
    /// extended profile, built from the same snapshot field, showed the real score. Assigned in
    /// UpdateWithPlayer, which is both the entry path and the player-updated path, so a change made
    /// during the visit reaches the avatar as well as the UserChange broadcast.
    /// </summary>
    public int ActivityPoints { get; private set; }

    public bool IsModerator { get; init; } = false;
    public override RoomObjectType AvatarType { get; } = RoomObjectType.Player;

    public required PlayerId PlayerId { get; init; }
    public AvatarGenderType Gender { get; private set; } = AvatarGenderType.Male;

    public bool UpdateWithPlayer(PlayerSummarySnapshot snapshot)
    {
        Name = snapshot.Name;
        Motto = snapshot.Motto;
        Figure = snapshot.Figure;
        Gender = snapshot.Gender;
        ActivityPoints = snapshot.AchievementScore;

        return true;
    }

    protected override RoomPlayerAvatarSnapshot BuildSnapshot()
    {
        StringBuilder statusString = new("/");

        foreach ((AvatarStatusType type, string value) in Statuses)
        {
            statusString.Append($"{type.ToLegacyString()} {value}/");
        }

        return new RoomPlayerAvatarSnapshot
        {
            AvatarType = AvatarType,
            WebId = PlayerId.Value,
            Name = Name,
            Motto = Motto,
            Figure = Figure,
            ObjectId = ObjectId,
            X = X,
            Y = Y,
            Z = Z,
            BodyRotation = Rotation,
            HeadRotation = HeadRotation,
            Status = statusString.ToString(),
            Gender = Gender,
            ActivityPoints = ActivityPoints,
            IsModerator = IsModerator,
            CarryItemId = CarryItemId,
        };
    }
}

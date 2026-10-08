using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;

namespace Vortex.Primitives.Rooms.Object.Avatars;

public interface IRoomPlayer : IRoomAvatar<IRoomPlayer, IRoomPlayerLogic, IRoomPlayerContext>
{
    new IRoomPlayerLogic Logic { get; }
    public PlayerId PlayerId { get; }
    public AvatarGenderType Gender { get; }
    public AvatarDanceType DanceType { get; }

    /// <summary>
    /// The achievement score the room was last told — the same number it broadcasts as
    /// <c>AchievementScore</c> on every user-change, kept here under the name the avatar block gives
    /// it. Written from the player summary at entry and again on every player update, never read
    /// live off the account.
    /// </summary>
    public int ActivityPoints { get; }

    public bool UpdateWithPlayer(PlayerSummarySnapshot snapshot);
    public bool SetDance(AvatarDanceType danceType = AvatarDanceType.None);
}

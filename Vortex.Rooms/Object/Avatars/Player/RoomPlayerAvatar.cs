using System.Text;
using Vortex.Primitives.Groups.Enums;
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
    /// <summary>The client's "no guild badge" sentinel.</summary>
    private const int NoGroup = -1;

    public int GroupId { get; private set; } = NoGroup;
    public int GroupStatus { get; private set; } = NoGroup;
    public string GroupName { get; private set; } = string.Empty;
    public string SwimFigure { get; init; } = string.Empty;

    /// <summary>
    /// The achievement score the avatar block reports. This was <c>init</c>-only and nothing ever
    /// initialised it — <c>RoomAvatarProvider.CreateAvatarFromPlayerSnapshot</c> builds the avatar
    /// with only ObjectId/PlayerId and then calls <see cref="UpdateWithPlayer"/> — so every avatar
    /// in every room serialised a hard 0 and the client's infostand showed 0 for a player whose
    /// extended profile, built from the same snapshot field, showed the real score. Assigned in
    /// UpdateWithPlayer, which is both the entry path and the player-updated path, so a gain made
    /// during the visit also reaches the room's own readers (the wired
    /// <c>UserAchievementScoreVariable</c>, the clothing booth) rather than only the UserChange
    /// broadcast.
    /// </summary>
    public int ActivityPoints { get; private set; }

    public bool IsModerator { get; init; } = false;
    public override RoomObjectType AvatarType { get; } = RoomObjectType.Player;

    public required PlayerId PlayerId { get; init; }
    public AvatarGenderType Gender { get; private set; } = AvatarGenderType.Male;
    public AvatarDanceType DanceType { get; private set; } = AvatarDanceType.None;

    /// <summary>
    /// The look this player had before a clothing-change booth dressed them, or null when no booth
    /// has.
    /// <para>
    /// It lives on the avatar and is never persisted, which is the whole of "the kit comes off when
    /// you leave": the avatar dies with the visit, so a player who walks out still wearing one is
    /// simply themselves again the next time a room draws them. The reference emulator needs a
    /// session cache and three event handlers for the same guarantee, because over there the
    /// override IS the saved look.
    /// </para>
    /// </summary>
    private string? _lookBeforeBooth;

    public bool UpdateWithPlayer(PlayerSummarySnapshot snapshot)
    {
        Name = snapshot.Name;
        Motto = snapshot.Motto;
        // The player saved a new look, so the booth's memory of the old one is now a look that no
        // longer exists anywhere. Dropping it here is what stops a later step off the booth putting
        // them back into a look they have already replaced.
        _lookBeforeBooth = null;
        Figure = snapshot.Figure;
        Gender = snapshot.Gender;
        ActivityPoints = snapshot.AchievementScore;

        SetFavouriteGroup(snapshot.FavouriteGroupId, snapshot.FavouriteGroupName);

        return true;
    }

    /// <summary>Puts the booth's outfit on, remembering the look underneath the first time.</summary>
    public void WearBoothOutfit(string figure)
    {
        _lookBeforeBooth ??= Figure;
        Figure = figure;
    }

    /// <summary>Puts the wearer back in their own look. False when no booth had dressed them, which
    /// is what tells a booth to dress rather than undress.</summary>
    public bool RemoveBoothOutfit()
    {
        if (_lookBeforeBooth is null)
        {
            return false;
        }

        Figure = _lookBeforeBooth;
        _lookBeforeBooth = null;

        return true;
    }

    /// <summary>
    /// Points the avatar's guild badge at <paramref name="groupId"/> (0 or less clears it). Returns
    /// true when something actually changed, so callers can skip a pointless room broadcast.
    /// </summary>
    public bool SetFavouriteGroup(int groupId, string groupName)
    {
        int resolvedId = groupId > 0 ? groupId : NoGroup;
        int resolvedStatus = groupId > 0 ? (int)GroupMembershipStatus.Member : NoGroup;
        string resolvedName = groupId > 0 ? groupName : string.Empty;

        if (GroupId == resolvedId && GroupStatus == resolvedStatus && GroupName == resolvedName)
        {
            return false;
        }

        GroupId = resolvedId;
        GroupStatus = resolvedStatus;
        GroupName = resolvedName;

        _snapshot = null;

        return true;
    }

    /// <summary>
    /// The dance sitting or laying interrupted, kept so standing up can resume it, or
    /// <see cref="AvatarDanceType.None"/> when the posture interrupted nothing.
    /// </summary>
    /// <remarks>
    /// Same shape as <see cref="_lookBeforeBooth"/> above and for the same reason: the avatar is
    /// the only thing that knows what was true a moment ago, and it dies with the visit, so a
    /// dance is never resumed in a later session. Without it the dance was dropped and gone —
    /// sitting cancelled it, standing had nothing to put back.
    /// </remarks>
    private AvatarDanceType _danceBeforeSit = AvatarDanceType.None;

    /// <summary>Sitting cancels the dance, like it does on Habbo — the dance is dropped before the
    /// status goes on so <see cref="SetDance"/> isn't blocked by its own sit guard, and stashed
    /// first so standing up can resume it.</summary>
    public override void Sit(bool flag = true, Altitude? height = null, Rotation? rot = null)
    {
        if (flag)
        {
            StashDanceForPosture();
        }

        base.Sit(flag, height, rot);

        if (!flag)
        {
            ResumeStashedDance();
        }
    }

    /// <inheritdoc cref="Sit"/>
    public override void Lay(bool flag = true, Altitude? height = null, Rotation? rot = null)
    {
        if (flag)
        {
            StashDanceForPosture();
        }

        base.Lay(flag, height, rot);

        if (!flag)
        {
            ResumeStashedDance();
        }
    }

    /// <summary>
    /// Remembers the current dance and stops it. Only the first posture of a run records anything:
    /// going from a chair straight onto a bed must not overwrite the stash with the
    /// <see cref="AvatarDanceType.None"/> the chair already left behind.
    /// </summary>
    private void StashDanceForPosture()
    {
        if (DanceType != AvatarDanceType.None)
        {
            _danceBeforeSit = DanceType;
        }

        SetDance(AvatarDanceType.None);
    }

    /// <summary>
    /// Puts the interrupted dance back, once the posture that cancelled it is actually off.
    /// </summary>
    /// <remarks>
    /// The stash is cleared only when <see cref="SetDance"/> accepts, which is what makes standing
    /// off a bed while still sitting — or any other order the map module walks the two postures in
    /// — harmless: SetDance refuses while either status is on, so the dance stays remembered until
    /// the avatar is really upright.
    /// </remarks>
    private void ResumeStashedDance()
    {
        if (_danceBeforeSit == AvatarDanceType.None)
        {
            return;
        }

        if (SetDance(_danceBeforeSit))
        {
            _danceBeforeSit = AvatarDanceType.None;
        }
    }

    public bool SetDance(AvatarDanceType danceType = AvatarDanceType.None)
    {
        if (DanceType == danceType)
        {
            return false;
        }

        if (HasStatus(AvatarStatusType.Sit, AvatarStatusType.Lay))
        {
            return false;
        }

        // check if dance valid
        // check if dance is hc only / validate hc

        DanceType = danceType;

        _snapshot = null;

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
            DanceType = DanceType,
            GroupId = GroupId,
            GroupStatus = GroupStatus,
            GroupName = GroupName,
            SwimFigure = SwimFigure,
            ActivityPoints = ActivityPoints,
            IsModerator = IsModerator,
            CurrentEffectId = CurrentEffectId,
            CarryItemId = CarryItemId,
            JumpPower = JumpPower,
        };
    }
}

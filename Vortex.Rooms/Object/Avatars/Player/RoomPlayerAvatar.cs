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

    /// <summary>Sitting cancels the dance, like it does on Habbo — the dance is dropped before the
    /// status goes on so <see cref="SetDance"/> isn't blocked by its own sit guard.</summary>
    public override void Sit(bool flag = true, Altitude? height = null, Rotation? rot = null)
    {
        if (flag)
        {
            SetDance(AvatarDanceType.None);
        }

        base.Sit(flag, height, rot);
    }

    /// <inheritdoc cref="Sit"/>
    public override void Lay(bool flag = true, Altitude? height = null, Rotation? rot = null)
    {
        if (flag)
        {
            SetDance(AvatarDanceType.None);
        }

        base.Lay(flag, height, rot);
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

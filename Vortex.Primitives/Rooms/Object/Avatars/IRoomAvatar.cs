using System.Collections.Generic;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Avatars;

namespace Vortex.Primitives.Rooms.Object.Avatars;

public interface IRoomAvatar<TSelf, out TLogic, out TContext>
    : IRoomObject<TSelf, TLogic, TContext>,
        IRoomAvatar
    where TSelf : IRoomAvatar<TSelf, TLogic, TContext>
    where TContext : IRoomAvatarContext<TSelf, TLogic, TContext>
    where TLogic : IRoomAvatarLogic<TSelf, TLogic, TContext>
{
    new TLogic Logic { get; }
}

public interface IRoomAvatar : IRoomObject
{
    new IRoomAvatarLogic Logic { get; }
    public RoomObjectType AvatarType { get; }
    public string Name { get; }
    public string Motto { get; }
    public string Figure { get; }
    public Rotation HeadRotation { get; }
    public Dictionary<AvatarStatusType, string> Statuses { get; }

    public Altitude PostureOffset { get; set; }
    public int GoalTileId { get; }
    public int NextTileId { get; set; }
    public bool IsWalking { get; set; }
    public bool NeedsInvoke { get; set; }
    public List<int> TilePath { get; }

    public long NextMoveStepAtMs { get; set; }

    /// <summary>Walking speed in percent of normal: below 100 the avatar skips steps (a slow).</summary>
    public int MoveSpeedPercent { get; set; }
    public long NextMoveUpdateAtMs { get; set; }
    public long PendingStopAtMs { get; set; }

    /// <summary>
    /// What the avatar is holding (0 = empty-handed): the weapon in hand of the RPG (spec §16.3).
    /// Broadcast via <c>CarryObjectMessageComposer</c> and re-synced to late joiners at room entry,
    /// the same way an effect is. Nothing sets it yet.
    /// </summary>
    public int CarryItemId { get; }

    /// <summary>True while the avatar may not start walking: stuns and roots (spec §6.5). Nothing
    /// sets it yet. Activation-scoped, so it dies with the avatar and can never leak between rooms
    /// or sessions.</summary>
    public bool IsMovementLocked { get; }

    public bool SetGoalTileId(int tileId);

    /// <summary>Sets or clears the movement lock. The caller is responsible for stopping an
    /// in-flight walk — the lock only gates NEW walks.</summary>
    public void SetMovementLocked(bool locked);

    /// <summary>Puts something in the hand, or empties it when zero.</summary>
    public bool SetCarryItem(int itemId);

    public void SetHeight(Altitude z);

    public void SetBodyRotation(Rotation rot);

    public void SetHeadRotation(Rotation rot);

    public void Sit(bool flag = true, Altitude? height = null, Rotation? rot = null);

    public void Lay(bool flag = true, Altitude? height = null, Rotation? rot = null);

    public void AddStatus(AvatarStatusType type, string value);

    public bool HasStatus(params AvatarStatusType[] types);

    public void RemoveStatus(params AvatarStatusType[] types);

    public RoomAvatarSnapshot GetSnapshot();
}

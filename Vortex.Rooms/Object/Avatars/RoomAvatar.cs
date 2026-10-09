using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Avatars;

namespace Vortex.Rooms.Object.Avatars;

public abstract class RoomAvatar<TSelf, TLogic, TContext>
    : RoomObject<TSelf, TLogic, TContext>,
        IRoomAvatar<TSelf, TLogic, TContext>
    where TSelf : IRoomAvatar<TSelf, TLogic, TContext>
    where TContext : IRoomAvatarContext<TSelf, TLogic, TContext>
    where TLogic : IRoomAvatarLogic<TSelf, TLogic, TContext>
{
    IRoomAvatarLogic IRoomAvatar.Logic => Logic;

    public abstract RoomObjectType AvatarType { get; }

    public string Name { get; protected set; } = string.Empty;
    public string Motto { get; protected set; } = string.Empty;
    public string Figure { get; protected set; } = string.Empty;

    public Rotation HeadRotation { get; protected set; }
    public Dictionary<AvatarStatusType, string> Statuses { get; } = [];

    public Altitude PostureOffset { get; set; } = Altitude.Zero;
    public int GoalTileId { get; private set; } = -1;
    public int NextTileId { get; set; } = -1;
    public bool IsWalking { get; set; } = false;
    public bool NeedsInvoke { get; set; } = false;
    public List<int> TilePath { get; } = [];

    public long NextMoveStepAtMs { get; set; } = 0;
    public int MoveSpeedPercent { get; set; } = 100;
    public long NextMoveUpdateAtMs { get; set; } = 0;
    public long PendingStopAtMs { get; set; } = 0;

    public int CurrentEffectId { get; private set; } = 0;

    public bool IsMovementLocked { get; private set; } = false;

    /// <summary>What the avatar is holding, or zero for empty-handed.</summary>
    public int CarryItemId { get; private set; } = 0;

    private int _goalTries = 0;

    protected RoomAvatarSnapshot? _snapshot;

    public bool SetGoalTileId(int tileId)
    {
        if (tileId == -1)
        {
            GoalTileId = -1;
            _goalTries = 0;

            return true;
        }

        if (tileId == GoalTileId)
        {
            _goalTries++;
        }
        else
        {
            GoalTileId = tileId;
            _goalTries = 0;
        }

        if (_goalTries == 3)
        {
            return false;
        }

        return true;
    }

    public void SetHeight(Altitude z)
    {
        z = Math.Round(z, 2);

        if (Z == z)
        {
            return;
        }

        Z = z;

        MarkDirty();
    }

    public new void SetRotation(Rotation rot)
    {
        SetBodyRotation(rot);
        SetHeadRotation(rot);
    }

    public void SetBodyRotation(Rotation rot)
    {
        if (Rotation == rot)
        {
            return;
        }

        Rotation = rot;

        MarkDirty();
    }

    public void SetHeadRotation(Rotation rot)
    {
        if (HeadRotation == rot)
        {
            return;
        }

        HeadRotation = rot;

        MarkDirty();
    }

    public virtual void Sit(bool flag = true, Altitude? height = null, Rotation? rot = null)
    {
        Altitude finalHeight = height ?? Altitude.FromValue(0.5);

        if (flag)
        {
            RemoveStatus(AvatarStatusType.Lay);

            rot ??= Rotation;

            SetRotation(rot.Value.ToSitRotation());
            AddStatus(AvatarStatusType.Sit, finalHeight.ToString());
        }
        else
        {
            if (!HasStatus(AvatarStatusType.Sit))
            {
                return;
            }

            RemoveStatus(AvatarStatusType.Sit);
        }
    }

    public virtual void Lay(bool flag = true, Altitude? height = null, Rotation? rot = null)
    {
        Altitude finalHeight = height ?? Altitude.FromValue(0.5);

        if (flag)
        {
            RemoveStatus(AvatarStatusType.Sit);

            rot ??= Rotation;

            SetRotation(rot.Value.ToSitRotation());
            AddStatus(AvatarStatusType.Lay, finalHeight.ToString());
        }
        else
        {
            if (!HasStatus(AvatarStatusType.Lay))
            {
                return;
            }

            RemoveStatus(AvatarStatusType.Lay);
        }
    }

    public void AddStatus(AvatarStatusType type, string value)
    {
        Statuses[type] = value;

        MarkDirty();
    }

    public bool HasStatus(params AvatarStatusType[] types) => types.Any(Statuses.ContainsKey);

    public void RemoveStatus(params AvatarStatusType[] types)
    {
        if (types.Length == 0)
        {
            return;
        }

        bool updated = false;

        foreach (AvatarStatusType type in types)
        {
            if (Statuses.Remove(type))
            {
                updated = true;
            }
        }

        if (updated)
        {
            MarkDirty();
        }
    }

    /// <summary>
    /// Puts something in the avatar's hand, or empties it when the item is zero. Returns true only
    /// when something changed, so handing somebody what they are already holding costs no broadcast.
    /// </summary>
    public bool SetCarryItem(int itemId)
    {
        itemId = itemId < 0 ? 0 : itemId;

        if (CarryItemId == itemId)
        {
            return false;
        }

        CarryItemId = itemId;
        _snapshot = null;

        return true;
    }

    public void SetMovementLocked(bool locked) => IsMovementLocked = locked;

    public bool SetEffect(int effectId)
    {
        effectId = effectId < 0 ? 0 : effectId;

        if (CurrentEffectId == effectId)
        {
            return false;
        }

        CurrentEffectId = effectId;
        _snapshot = null;

        return true;
    }

    public RoomAvatarSnapshot GetSnapshot()
    {
        if (_dirty || _snapshot is null)
        {
            _snapshot = BuildSnapshot();
            _dirty = false;
        }

        return _snapshot;
    }

    protected abstract RoomAvatarSnapshot BuildSnapshot();
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Logging;
using Vortex.Logging.Extensions;
using Vortex.Primitives;
using Vortex.Primitives.Events;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Mapping;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Protocol.Messages.Outgoing.Room.Action;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;

namespace Vortex.Rooms.Grains.Systems;

public sealed class RoomAvatarTickSystem(RoomGrain roomGrain)
{
    private readonly RoomGrain _roomGrain = roomGrain;

    // Reused across ticks — RoomAvatarTickSystem is one instance per room grain, which is
    // single-threaded, so clearing and refilling this per tick is safe and avoids a per-tick
    // allocation proportional to room count × tick rate.
    private readonly List<RoomAvatarSnapshot> _dirtySnapshots = [];

    public async Task ProcessAvatarsAsync(long now, CancellationToken ct)
    {
        if (now < _roomGrain._state.NextAvatarBoundaryMs)
        {
            return;
        }

        _roomGrain._state.NextAvatarBoundaryMs = _roomGrain.AdvanceBoundaryPast(
            now,
            _roomGrain._roomConfig.AvatarTickMs
        );

        _dirtySnapshots.Clear();

        foreach (IRoomAvatar avatar in _roomGrain._state.AvatarsByObjectId.Values)
        {
            try
            {
                await _roomGrain.AvatarModule.ProcessNextAvatarStepAsync(avatar, ct);

                if (avatar.TilePath.Count <= 0)
                {
                    if (avatar.PendingStopAtMs > 0 && now < avatar.PendingStopAtMs)
                    {
                        continue;
                    }

                    await _roomGrain.AvatarModule.StopWalkingAsync(avatar, ct);

                    if (avatar.NeedsInvoke)
                    {
                        await _roomGrain.MapModule.InvokeAvatarAsync(avatar, ct);
                    }
                }
                else
                {
                    await ProcessAvatarAsync(avatar, now, ct);
                }

                if (avatar.IsDirty)
                {
                    _dirtySnapshots.Add(avatar.GetSnapshot());
                }
            }
            catch (Exception ex)
            {
                _roomGrain._logger.LogWarning(
                    ex,
                    "Failed to process avatar tick for avatar {ObjectId} in room {RoomId}.",
                    avatar.ObjectId,
                    _roomGrain.RoomId
                );
            }
        }

        if (_dirtySnapshots.Count == 0)
        {
            return;
        }

        _roomGrain
            .SendComposerToRoomAsync(
                new UserUpdateMessageComposer { Avatars = [.. _dirtySnapshots] }
            )
            .LogAndForget(
                _roomGrain._logger,
                "Failed to publish avatar tick update for room {RoomId}",
                _roomGrain.RoomId
            );
    }

    private async Task ProcessAvatarAsync(IRoomAvatar avatar, long now, CancellationToken ct)
    {
        int nextTileId = avatar.TilePath[0];
        avatar.TilePath.RemoveAt(0);

        if (avatar.TilePath.Count == 0)
        {
            avatar.PendingStopAtMs = _roomGrain.AlignToNextBoundary(
                now,
                _roomGrain._roomConfig.AvatarTickMs
            );
        }

        await ValidateAvatarStepAsync(avatar, nextTileId, now, ct);
    }

    private async Task ValidateAvatarStepAsync(
        IRoomAvatar avatar,
        int nextTileId,
        long now,
        CancellationToken ct
    )
    {
        try
        {
            bool isGoal = avatar.TilePath.Count == 0;
            int prevTileId = _roomGrain.MapModule.ToIdx(avatar.X, avatar.Y);
            (int nextX, int nextY) = _roomGrain.MapModule.GetTileXY(nextTileId);

            // The step is measured from where the avatar's feet actually are, not from the tile's
            // highest surface: that is what keeps somebody walking under a platform under it rather
            // than snapping them onto its roof for a step, and it is why this passes avatar.Z.
            //
            // It is the altitude of the tile just left, because ProcessNextAvatarStepAsync() moves
            // X and Y first and settles Z afterwards.
            //
            // The step-height rule is inside that search too. It used to be repeated here and only
            // here -- enforced while walking but not while the path was planned -- so the
            // pathfinder routed straight up a cliff and the walk was aborted at the first tile it
            // could not climb, leaving the avatar wherever the doomed path had got to.
            if (
                !_roomGrain.MapModule.CanAvatarWalkBetween(
                    avatar,
                    prevTileId,
                    nextTileId,
                    avatar.Z,
                    out RoomTileSection nextSection,
                    isGoal
                )
            )
            {
                // A monster is re-pathed by its own AI, within its rules: it just stops here.
                if (!isGoal && avatar is not IRoomMonster)
                {
                    (int goalX, int goalY) = _roomGrain.MapModule.GetTileXY(avatar.GoalTileId);

                    if (await _roomGrain.AvatarModule.WalkAvatarToAsync(avatar, goalX, goalY, ct))
                    {
                        await ProcessAvatarAsync(avatar, now, ct);

                        return;
                    }
                }

                // A step the map refuses is the ordinary end of a walk, not a failure: the tile was
                // taken, raised or closed while the avatar was on its way, and there is no longer a
                // path to the goal. Stop and say nothing. This used to throw and be caught below,
                // which cost an exception and a warning line per blocked avatar per tick, and made
                // a real fault in the walk path look exactly like a tile somebody else stepped on.
                await _roomGrain.AvatarModule.StopWalkingAsync(avatar, ct);

                return;
            }

            // A zone's Restricted area stops a player at its edge; a monster never steps on a safe
            // cell (spec M1a §5.3), even if one turned safe under its path.
            if (
                !_roomGrain.ZoneAreas.MayStepOnto(avatar, nextX, nextY)
                || avatar is IRoomMonster && _roomGrain.CombatRuntime.RulesAt(nextX, nextY).Safe
            )
            {
                await _roomGrain.AvatarModule.StopWalkingAsync(avatar, ct);

                return;
            }

            RoomObjectId prevHighestItemId = _roomGrain._state.TileHighestFloorItems[prevTileId];
            // The item walked *onto*, which on a tile with two surfaces is not the one on top: step
            // under a platform and the walk-on trigger that fires is the floor's, not the roof's.
            RoomObjectId nextHighestItemId = nextSection.ItemId;

            // A monster has no object logic: no furniture reacts to it walking on or off.
            if (
                avatar.Logic is not null
                && prevHighestItemId > 0
                && _roomGrain._state.ItemsById.TryGetValue(
                    prevHighestItemId,
                    out IRoomItem? prevFloorItem
                )
            )
            {
                await ((IRoomFloorItem)prevFloorItem).Logic.OnWalkOffAsync(
                    (IRoomAvatarContext)avatar.Logic.Context,
                    ct
                );
            }

            _roomGrain.MapModule.RemoveAvatarAtIdx(avatar, prevTileId, false);
            _roomGrain.MapModule.AddAvatarAtIdx(avatar, nextTileId, false);

            if (
                avatar.Logic is not null
                && nextHighestItemId > 0
                && _roomGrain._state.ItemsById.TryGetValue(
                    nextHighestItemId,
                    out IRoomItem? nextFloorItem
                )
            )
            {
                await ((IRoomFloorItem)nextFloorItem).Logic.OnWalkOnAsync(
                    (IRoomAvatarContext)avatar.Logic.Context,
                    ct
                );
            }

            // The height the client is told to walk to is the section's, less whatever the item
            // under it sinks the pose by -- the same arithmetic GetTileHeightForAvatar() does, but
            // about the surface this step chose rather than the tile's top.
            Altitude nextHeight = _roomGrain.MapModule.GetHeightForAvatarOn(nextSection);

            avatar.RemoveStatus(AvatarStatusType.Lay, AvatarStatusType.Sit);
            avatar.AddStatus(AvatarStatusType.Move, $"{nextX},{nextY},{nextHeight}");
            avatar.SetRotation(RotationExtensions.FromPoints(avatar.X, avatar.Y, nextX, nextY));

            avatar.NextTileId = nextTileId;
        }
        // Nothing above throws to say "this step is blocked" any more, so whatever lands here is a
        // genuine fault in the walk path and is logged as one. Keep it: a room that cannot advance
        // one avatar must not stop ticking the others.
        catch (Exception ex)
        {
            _roomGrain._logger.LogError(
                ex,
                "Failed to advance avatar {ObjectId} to next tile in room {RoomId}; stopping walk.",
                avatar.ObjectId,
                _roomGrain.RoomId
            );

            await _roomGrain.AvatarModule.StopWalkingAsync(avatar, ct);
        }
    }
}

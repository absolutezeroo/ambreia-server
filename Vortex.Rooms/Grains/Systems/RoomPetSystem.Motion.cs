using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Pets;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Protocol.Messages.Outgoing.Inventory.Pets;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Room.Pets;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.Rooms.Grains.Systems;

public sealed partial class RoomPetSystem
{
    private async Task<RoomPetAvatarSnapshot?> ProcessPetMotionAsync(
        PetSnapshot pet,
        PetMotionState motion,
        long now,
        CancellationToken ct
    )
    {
        if (motion.PendingStopAtMs > 0 && motion.TilePath.Count == 0)
        {
            if (now < motion.PendingStopAtMs)
            {
                return null;
            }

            motion.PendingStopAtMs = 0;
            motion.NextWanderAtMs = ScheduleNextWanderAt(now);

            return await ToAvatarSnapshotAsync(pet, ct);
        }

        // The owner first: a companion that has fallen behind goes back to them, checked on every
        // idle tick so it does not stand around while they walk off. Close by, it wanders.
        if (
            motion.TilePath.Count == 0
            && !TryFollowOwner(pet, motion, now)
            && now >= motion.NextWanderAtMs
        )
        {
            TryStartWander(pet, motion, now);
        }

        if (motion.TilePath.Count == 0)
        {
            return null;
        }

        int nextTileId = motion.TilePath[0];
        motion.TilePath.RemoveAt(0);

        if (motion.TilePath.Count == 0)
        {
            motion.PendingStopAtMs = _roomGrain.AlignToNextBoundary(
                now,
                _roomGrain._roomConfig.Pet.TickMs
            );
        }

        if (
            !TryPreparePetStep(
                pet,
                motion,
                nextTileId,
                out PetSnapshot facingPet,
                out string status
            )
        )
        {
            motion.ClearMovement();
            motion.NextWanderAtMs = ScheduleNextWanderAt(now);

            return await ToAvatarSnapshotAsync(pet, ct);
        }

        _roomGrain._state.PetsById[pet.PetId] = facingPet;

        return await ToAvatarSnapshotAsync(facingPet, status, ct);
    }

    private PetSnapshot ApplyPendingPetStep(PetSnapshot pet, PetMotionState motion)
    {
        if (motion.NextTileId < 0)
        {
            return pet;
        }

        int nextTileId = motion.NextTileId;
        motion.NextTileId = -1;

        if (!_roomGrain.MapModule.InBounds(nextTileId))
        {
            return pet;
        }

        (int nextX, int nextY) = _roomGrain.MapModule.GetTileXY(nextTileId);

        if (pet.X == nextX && pet.Y == nextY)
        {
            return pet;
        }

        Altitude nextHeight = _roomGrain._state.TileHeights[nextTileId];
        Rotation direction = RotationExtensions.FromPoints(pet.X, pet.Y, nextX, nextY);
        PetSnapshot updated = pet with
        {
            X = nextX,
            Y = nextY,
            Z = nextHeight.Value,
            Direction = direction,
        };

        _roomGrain._state.PetsById[pet.PetId] = updated;

        return updated;
    }

    private bool TryPreparePetStep(
        PetSnapshot pet,
        PetMotionState motion,
        int nextTileId,
        out PetSnapshot facingPet,
        out string status
    )
    {
        facingPet = pet;
        status = string.Empty;

        int currentTileId = _roomGrain.MapModule.ToIdx(pet.X, pet.Y);

        if (!CanPetWalkBetween(pet.PetId, currentTileId, nextTileId, motion.TilePath.Count == 0))
        {
            return false;
        }

        (int nextX, int nextY) = _roomGrain.MapModule.GetTileXY(nextTileId);

        if (pet.X == nextX && pet.Y == nextY)
        {
            return false;
        }

        Altitude nextHeight = _roomGrain._state.TileHeights[nextTileId];
        Rotation direction = RotationExtensions.FromPoints(pet.X, pet.Y, nextX, nextY);

        facingPet = pet with { Direction = direction };
        motion.NextTileId = nextTileId;
        status = $"/{AvatarStatusType.Move.ToLegacyString()} {nextX},{nextY},{nextHeight}/";

        return true;
    }

    private bool TryStartWander(PetSnapshot pet, PetMotionState motion, long now)
    {
        motion.NextWanderAtMs = ScheduleNextWanderAt(now);

        if (!_roomGrain.MapModule.InBounds(pet.X, pet.Y))
        {
            return false;
        }

        int radius = Math.Max(1, _roomGrain._roomConfig.Pet.WanderRadius);
        int attempts = Math.Max(1, _roomGrain._roomConfig.Pet.WanderCandidateAttempts);

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            int targetX = pet.X + Random.Shared.Next(-radius, radius + 1);
            int targetY = pet.Y + Random.Shared.Next(-radius, radius + 1);

            if (
                (targetX == pet.X && targetY == pet.Y)
                || !_roomGrain.MapModule.InBounds(targetX, targetY)
            )
            {
                continue;
            }

            if (TryWalkTo(pet, motion, targetX, targetY))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Walks a pet that has strayed more than <see cref="Configuration.PetConfig.FollowDistance" />
    /// tiles from its owner back to the nearest free tile beside them. False when the owner is not
    /// in the room, is close enough already, or cannot be reached.
    /// </summary>
    private bool TryFollowOwner(PetSnapshot pet, PetMotionState motion, long now)
    {
        if (
            !_roomGrain._state.AvatarsByPlayerId.TryGetValue(pet.OwnerId, out RoomObjectId objectId)
            || !_roomGrain._state.AvatarsByObjectId.TryGetValue(objectId, out IRoomAvatar? owner)
            || RoomPetRuntime.IsWithin(
                pet.X,
                pet.Y,
                owner.X,
                owner.Y,
                _roomGrain._roomConfig.Pet.FollowDistance
            )
        )
        {
            return false;
        }

        (int X, int Y)? beside = RoomPetRuntime.PickNearestTile(
            pet.X,
            pet.Y,
            RoomPetRuntime
                .Neighbours(owner.X, owner.Y)
                .Where(tile =>
                    _roomGrain.MapModule.InBounds(tile.X, tile.Y)
                    && CanPetOccupyTile(pet.PetId, _roomGrain.MapModule.ToIdx(tile.X, tile.Y))
                )
        );

        if (beside is null || !TryWalkTo(pet, motion, beside.Value.X, beside.Value.Y))
        {
            return false;
        }

        motion.NextWanderAtMs = ScheduleNextWanderAt(now);

        return true;
    }

    /// <summary>Paths the pet to a tile it may stand on; false when there is no way there.</summary>
    private bool TryWalkTo(PetSnapshot pet, PetMotionState motion, int targetX, int targetY)
    {
        if (!CanPetOccupyTile(pet.PetId, _roomGrain.MapModule.ToIdx(targetX, targetY)))
        {
            return false;
        }

        IReadOnlyList<(int X, int Y)> path = _roomGrain.PathingSystem.FindPath(
            (pet.X, pet.Y),
            (targetX, targetY),
            tileId => CanPetOccupyTile(pet.PetId, tileId),
            (currentTileId, nextTileId, isGoal) =>
                CanPetWalkBetween(pet.PetId, currentTileId, nextTileId, isGoal)
        );

        if (path.Count < 2)
        {
            return false;
        }

        motion.TilePath.Clear();
        motion.TilePath.AddRange(
            path.Skip(1).Select(pos => _roomGrain.MapModule.ToIdx(pos.X, pos.Y))
        );

        return true;
    }

    private bool CanPetWalkBetween(int petId, int currentTileId, int nextTileId, bool isGoal)
    {
        if (!CanPetOccupyTile(petId, nextTileId))
        {
            return false;
        }

        Altitude currentHeight = _roomGrain._state.TileHeights[currentTileId];
        Altitude nextHeight = _roomGrain._state.TileHeights[nextTileId];

        if (Math.Abs(nextHeight - currentHeight) > Math.Abs(_roomGrain._roomConfig.MaxStepHeight))
        {
            return false;
        }

        if (
            !_roomGrain._roomConfig.EnableDiagonalChecking
            || !_roomGrain.MapModule.IsDiagonal(currentTileId, nextTileId)
        )
        {
            return true;
        }

        (int fromX, int fromY) = _roomGrain.MapModule.GetTileXY(currentTileId);
        (int toX, int toY) = _roomGrain.MapModule.GetTileXY(nextTileId);
        bool left = CanPetOccupyTile(petId, _roomGrain.MapModule.ToIdx(toX, fromY));
        bool right = CanPetOccupyTile(petId, _roomGrain.MapModule.ToIdx(fromX, toY));

        return left || right;
    }

    private bool CanPetOccupyTile(int petId, int tileIdx)
    {
        if (!_roomGrain.MapModule.InBounds(tileIdx))
        {
            return false;
        }

        RoomTileFlags flags = _roomGrain._state.TileFlags[tileIdx];

        if (
            flags.Has(RoomTileFlags.Disabled)
            || flags.Has(RoomTileFlags.Closed)
            || flags.Has(RoomTileFlags.AvatarOccupied)
        )
        {
            return false;
        }

        if (flags.Has(RoomTileFlags.FurnitureOccupied) && !flags.Has(RoomTileFlags.Walkable))
        {
            return false;
        }

        return !IsPetTileOccupied(petId, tileIdx);
    }

    private bool IsPetTileOccupied(int petId, int tileIdx)
    {
        foreach (PetSnapshot pet in _roomGrain._state.PetsById.Values)
        {
            if (pet.PetId == petId)
            {
                continue;
            }

            if (_roomGrain.MapModule.ToIdx(pet.X, pet.Y) == tileIdx)
            {
                return true;
            }
        }

        return false;
    }
}

using System;
using System.Collections.Generic;
using Vortex.Database.Entities.Pets;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Snapshots.Avatars;

namespace Vortex.Rooms.Grains.Systems;

internal static class RoomPetRuntime
{
    private const int PetRoomObjectIdOffset = 1_000_000;

    /// <summary>The coordinates the client sends when a pet is dropped from the inventory rather
    /// than aimed at a tile.</summary>
    public const int InventoryDropX = 0;
    public const int InventoryDropY = 0;

    public static PetSnapshot ToSnapshot(PetEntity entity) =>
        new()
        {
            PetId = entity.Id,
            OwnerId = new PlayerId(entity.OwnerPlayerEntityId),
            RoomId = entity.RoomEntityId,
            Name = entity.Name,
            Type = entity.Type,
            Skin = entity.Skin,
            Color = entity.Color,
            Gender = entity.Gender,
            Level = entity.Level,
            Experience = entity.Experience,
            CreatedAt = entity.CreatedAt,
            HasSaddle = entity.HasSaddle,
            SaddleDefinitionId = entity.SaddleDefinitionId,
            RidingPermission = entity.RidingPermission,
            X = entity.X,
            Y = entity.Y,
            Z = entity.Z,
            Direction = (Rotation)entity.Direction,
        };

    public static RoomObjectId ToRoomObjectId(int petId) => petId + PetRoomObjectIdOffset;

    public static RoomPetAvatarSnapshot ToAvatarSnapshot(
        PetSnapshot pet,
        string ownerName,
        string status = "",
        string posture = PetAnimation.StandPosture
    ) =>
        new()
        {
            AvatarType = RoomObjectType.Pet,
            WebId = pet.PetId,
            Name = pet.Name,
            Motto = string.Empty,
            Figure = ToFigureString(pet),
            ObjectId = ToRoomObjectId(pet.PetId),
            X = pet.X,
            Y = pet.Y,
            Z = pet.Z,
            BodyRotation = pet.Direction,
            HeadRotation = pet.Direction,
            Status = status,
            SubType = pet.Type,
            OwnerId = pet.OwnerId.Value,
            OwnerName = ownerName,
            HasSaddle = pet.HasSaddle,
            IsRiding = pet.RiderId is not null,
            PetLevel = pet.Level,
            PetPosture = posture,
        };

    /// <summary>
    /// Where a pet dropped from the inventory lands.
    ///
    /// The client hard-codes (0, 0) for that drop -- there is no tile to name yet -- so those
    /// coordinates mean "anywhere that works", not the corner of the room. Read literally they
    /// rejected the placement whenever tile 0,0 was blocked, which is most rooms, and left
    /// non-owners unable to place a pet at all where the room allowed them.
    ///
    /// Any other coordinates are the player pointing at a tile and are honoured as given.
    ///
    /// The search runs outwards from the door in rings, so a pet appears near where players arrive
    /// rather than in whichever corner is scanned first.
    /// </summary>
    public static bool TryResolveDropTile(
        int x,
        int y,
        int doorX,
        int doorY,
        int width,
        int height,
        Func<int, int, bool> isFree,
        out int resolvedX,
        out int resolvedY
    )
    {
        resolvedX = x;
        resolvedY = y;

        if (x != InventoryDropX || y != InventoryDropY)
        {
            return isFree(x, y);
        }

        int reach = Math.Max(width, height);

        for (int radius = 0; radius <= reach; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    // Only the ring at this radius; the inside was covered by earlier passes.
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    if (!isFree(doorX + dx, doorY + dy))
                    {
                        continue;
                    }

                    resolvedX = doorX + dx;
                    resolvedY = doorY + dy;

                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether two tiles are at most <paramref name="distance" /> steps apart, diagonals
    /// counting as one step -- the way a pet walks.</summary>
    public static bool IsWithin(int fromX, int fromY, int toX, int toY, int distance) =>
        Math.Max(Math.Abs(fromX - toX), Math.Abs(fromY - toY)) <= distance;

    /// <summary>The eight tiles around a tile, bounds unchecked.</summary>
    public static IEnumerable<(int X, int Y)> Neighbours(int x, int y)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx != 0 || dy != 0)
                {
                    yield return (x + dx, y + dy);
                }
            }
        }
    }

    /// <summary>
    /// The nearest candidate tile by walking distance, or null when there is none. Ties keep the
    /// first candidate, so a pet with two equidistant tiles beside its owner picks the same one every tick rather
    /// than dithering between them.
    /// </summary>
    public static (int X, int Y)? PickNearestTile(
        int fromX,
        int fromY,
        IEnumerable<(int X, int Y)> candidates
    )
    {
        (int X, int Y)? best = null;
        int bestDist = int.MaxValue;

        foreach ((int X, int Y) candidate in candidates)
        {
            int dist = Math.Abs(fromX - candidate.X) + Math.Abs(fromY - candidate.Y);

            if (dist >= bestDist)
            {
                continue;
            }

            bestDist = dist;
            best = candidate;
        }

        return best;
    }

    private static string ToFigureString(PetSnapshot pet) => $"{pet.Type} {pet.Skin} {pet.Color} 0";
}

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
            Race = entity.Race,
            Color = entity.Color,
            Gender = entity.Gender,
            Level = entity.Level,
            Experience = entity.Experience,
            Energy = entity.Energy,
            Nutrition = entity.Nutrition,
            Thirst = entity.Thirst,
            Respect = entity.Respect,
            Happiness = entity.Happiness,
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

    /// <summary>
    /// How likely a pet is to obey one order, in percent. A capture of the live hotel caught a pet
    /// ignoring four orders out of nine and saying so, so a pet that always obeys is wrong; what the
    /// hotel's real rule is, nine samples do not say. This is Arcturus' shape -- a base chance plus
    /// a bonus for every level the pet has over the command's own requirement, under a ceiling --
    /// and all three numbers are server configuration precisely because they are assumed.
    ///
    /// A base of 100 restores the old always-obeys behaviour, and the ceiling is what stops a
    /// long-outgrown trick from becoming automatic.
    /// </summary>
    public static int ObeyChancePercent(
        int petLevel,
        int levelRequired,
        int basePercent,
        int perLevelPercent,
        int maxPercent
    )
    {
        int bonus = Math.Max(0, petLevel - levelRequired) * perLevelPercent;

        return Math.Clamp(Math.Min(basePercent + bonus, maxPercent), 0, 100);
    }

    /// <summary>
    /// The command that is an errand rather than a pose: the pet has to walk to its nest for it to
    /// mean anything. The id is the client's own, from <c>pet.command.&lt;id&gt;</c>.
    /// </summary>
    public const int NestCommandId = 13;

    /// <summary>Whether a pet is bored enough to go and find a toy.</summary>
    public static bool IsBored(PetSnapshot pet, int boredHappinessThreshold) =>
        pet.Happiness <= boredHappinessThreshold;

    /// <summary>
    /// The nearest candidate tile by walking distance, or null when there is none. Ties keep the
    /// first candidate, so a room with two equidistant nests picks the same one every tick rather
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

    /// <summary>
    /// Takes the whole points a need has accrued since its own clock, and moves that clock forward
    /// by only the time those points cost -- never to <paramref name="now" />, so the leftover
    /// fraction carries into the next tick.
    /// </summary>
    /// <remarks>
    /// Happiness is the one stat left on a clock since M0 removed hunger, thirst and fatigue.
    /// </remarks>
    public static int TakeWholeNeedPoints(
        long clockMs,
        long now,
        double perMinute,
        out long nextClockMs
    )
    {
        nextClockMs = clockMs;

        if (perMinute <= 0)
        {
            nextClockMs = now;

            return 0;
        }

        long elapsedMs = now - clockMs;

        if (elapsedMs <= 0)
        {
            return 0;
        }

        int points = (int)(elapsedMs / 60_000.0 * perMinute);

        if (points <= 0)
        {
            return 0;
        }

        nextClockMs = clockMs + (long)(points / perMinute * 60_000.0);

        return points;
    }

    private static string ToFigureString(PetSnapshot pet) => $"{pet.Type} {pet.Race} {pet.Color} 0";
}

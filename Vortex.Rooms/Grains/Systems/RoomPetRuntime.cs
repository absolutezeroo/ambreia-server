using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Furniture;
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

    private const int MonsterplantPetType = 16;
    private const int MonsterplantMaxLevel = 7;

    /// <summary>Thirst runs 0-100 like the other needs.</summary>
    public const int ThirstCap = 100;

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
            ParentOneId = entity.ParentOneId,
            ParentTwoId = entity.ParentTwoId,
            CanBreed = entity.CanBreed,
            RarityLevel = entity.RarityLevel,
            LastWateredAt = entity.LastWateredAt,
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
        string posture = StandPosture
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
            RarityLevel = pet.RarityLevel,
            HasSaddle = pet.HasSaddle,
            IsRiding = pet.RiderId is not null,
            CanBreed = CanBreed(pet),
            CanHarvest = CanHarvest(pet),
            CanRevive = CanRevive(pet),
            HasBreedingPermission = HasBreedingPermission(pet),
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
    /// Which actions a pet offers. Shared with the status push so the room list and the status
    /// message can never disagree about whether a pet can be bred, harvested or revived.
    /// </summary>
    public static bool CanBreed(PetSnapshot pet) => pet.Type != MonsterplantPetType && pet.CanBreed;

    /// <summary>
    /// A grown plant offers its seed once. <c>CanBreed</c> is that charge: harvesting spends it and
    /// a rebreed potion restores it, which is why the button disappears in between rather than
    /// staying and doing nothing.
    /// </summary>
    public static bool CanHarvest(PetSnapshot pet) =>
        pet is { Type: MonsterplantPetType, Level: >= MonsterplantMaxLevel }
        && pet.Energy > 0
        && pet.CanBreed;

    public static bool CanRevive(PetSnapshot pet) =>
        pet.Type == MonsterplantPetType && pet.Energy == 0;

    public static bool HasBreedingPermission(PetSnapshot pet) =>
        pet.Type != MonsterplantPetType && pet.CanBreed;

    /// <summary>
    /// Whether a pet is a monsterplant. Named once because half the pet rules fork on it — a plant
    /// is watered rather than scratched, never sleeps, never plays and cannot be bred at a nest.
    /// </summary>
    public static bool IsPlant(PetSnapshot pet) => pet.Type == MonsterplantPetType;

    /// <summary>
    /// A monsterplant's well-being, read off the watering stamp rather than accumulated: the client
    /// counts the same window down from the same stamp, so deriving it keeps the two in step even
    /// after the room has been unloaded for hours.
    /// </summary>
    public static int PlantWellBeing(
        DateTime wateredAt,
        DateTime now,
        int energyCap,
        int wellBeingSeconds
    )
    {
        if (wellBeingSeconds <= 0)
        {
            return energyCap;
        }

        double remaining = 1.0 - ((now - wateredAt).TotalSeconds / wellBeingSeconds);

        return (int)Math.Round(energyCap * Math.Clamp(remaining, 0.0, 1.0));
    }

    /// <summary>
    /// The growth stage banked seconds add up to. Stage 1 is a fresh sprout, so the seconds only
    /// start counting from there.
    /// </summary>
    public static int PlantLevelFor(int grownSeconds, int growthSeconds, int maxLevel) =>
        growthSeconds <= 0
            ? maxLevel
            : Math.Clamp(1 + (Math.Max(0, grownSeconds) / growthSeconds), 1, maxLevel);

    /// <summary>
    /// What a pet is doing when it is doing nothing. The pet asset's own default animation id, and
    /// what the client falls back to for any posture it does not recognise.
    /// </summary>
    public const string StandPosture = "std";

    /// <summary>The posture a pet takes to sleep. One of the ids the pet's own asset declares.</summary>
    public const string LayPosture = "lay";

    /// <summary>Playing with a toy. Declared by the pet assets as `pla`.</summary>
    public const string PlayPosture = "pla";

    /// <summary>
    /// The two commands that are an errand rather than a pose: the pet has to walk somewhere for
    /// them to mean anything. Ids are the client's own, from <c>pet.command.&lt;id&gt;</c>.
    /// </summary>
    public const int NestCommandId = 13;
    public const int EatCommandId = 43;

    /// <summary>
    /// The posture a pet takes at a bowl. Drinking uses it too: no pet asset declares a `drk`
    /// posture -- the dog ships std, beg, bnd, ded, eat, jmp, lay, pla, rdy, scr, sit, snf, spk, mv
    /// -- and an unknown posture silently resolves to the default, which is standing. Arcturus sends
    /// EAT at the water bowl for the same reason.
    /// </summary>
    public const string EatPosture = "eat";

    /// <summary>
    /// A status segment the client will actually keep.
    /// </summary>
    /// <remarks>
    /// The client drops any segment that carries no value: it splits on a space and only records the
    /// action when there are two pieces (<c>if (pieces.length >= 2)</c> in the nitro parser, which is
    /// what this revision speaks). So a bare <c>/lay/</c> or <c>/eat/</c> never becomes a posture at
    /// all, and the pet stays standing -- no lying down, no Zzz, no eating. Arcturus never sends one
    /// either: it passes the tile's stack height. The height doubles as the value the client reads
    /// back for <c>lay</c> and <c>sit</c>, so it has to be the real one, formatted invariantly.
    /// </remarks>
    public static string StatusWithHeight(string posture, double height) =>
        $"/{posture} {height.ToString("0.##", CultureInfo.InvariantCulture)}/";

    public static string LayStatus(double height) => StatusWithHeight(LayPosture, height);

    public static string EatStatus(double height) => StatusWithHeight(EatPosture, height);

    public static string PlayStatus(double height) => StatusWithHeight(PlayPosture, height);

    /// <summary>
    /// Whether a pet is bored enough to go and find a toy. Monsterplants are excluded: they are
    /// rooted, and nothing about them plays.
    /// </summary>
    public static bool IsBored(PetSnapshot pet, int boredHappinessThreshold) =>
        pet.Type != MonsterplantPetType && pet.Happiness <= boredHappinessThreshold;

    /// <summary>
    /// Whether a bowl still has something in it, from the point of view of a pet deciding to walk
    /// over. State doubles as the serving counter, counting down to an empty frame.
    /// </summary>
    /// <remarks>
    /// A furni with one state has no frame to count down through: its only valid state is 0, which
    /// would otherwise read as empty and make the fifteen one-state bowls invisible to every pet.
    /// Those carry their servings in the pet_food row instead.
    /// </remarks>
    public static bool HasServingsLeft(int totalStates, int state) => totalStates <= 1 || state > 0;

    /// <summary>
    /// Whether a pet reads as tired: it should stop wandering and go nap on a nest. Monsterplants
    /// are excluded -- they are rooted, and their energy means watering, not sleep.
    /// </summary>
    public static bool IsTired(PetSnapshot pet, int tiredEnergyThreshold) =>
        pet.Type != MonsterplantPetType && pet.Energy <= tiredEnergyThreshold;

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
    /// Nutrition and energy each need their own clock. Sharing one and resetting it to now the
    /// moment either need ticked let the faster need starve the slower one: nutrition at 1.0/min
    /// reset the accumulator every minute, so energy at 0.5/min never reached a whole point. Pets
    /// never tired, never got thirsty and never fell asleep.
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

    private static string ToFigureString(PetSnapshot pet) =>
        pet.Type == MonsterplantPetType
            ? $"{pet.Type} {pet.Level} {pet.Color} 0"
            : $"{pet.Type} {pet.Race} {pet.Color} 0";

    public static async Task<PetFeedResult> FeedAsync(
        VortexDbContext dbCtx,
        int roomId,
        int actorPlayerId,
        int petId,
        RoomObjectId foodItemId,
        bool allowPetsEat,
        int nutritionCap,
        int energyCap,
        CancellationToken ct
    )
    {
        if (!allowPetsEat)
        {
            return PetFeedResult.Failed(foodItemId);
        }

        PetEntity? pet = await dbCtx.Pets.SingleOrDefaultAsync(
            p => p.Id == petId && p.RoomEntityId == roomId && p.DeletedAt == null,
            ct
        );

        if (pet is null || pet.OwnerPlayerEntityId != actorPlayerId)
        {
            return PetFeedResult.Failed(foodItemId);
        }

        FurnitureEntity? food = await dbCtx.Furnitures.SingleOrDefaultAsync(
            f => f.Id == foodItemId.Value && f.RoomEntityId == roomId && f.DeletedAt == null,
            ct
        );

        if (food is null)
        {
            return PetFeedResult.Failed(foodItemId);
        }

        PetFoodEntity? petFood = await dbCtx
            .PetFood.AsNoTracking()
            .SingleOrDefaultAsync(
                f =>
                    f.FurnitureDefinitionEntityId == food.FurnitureDefinitionEntityId
                    && f.PetType == pet.Type
                    && f.DeletedAt == null,
                ct
            );

        if (
            petFood is null
            || (petFood.Nutrition <= 0 && petFood.Energy <= 0 && petFood.Thirst <= 0)
        )
        {
            return PetFeedResult.Failed(foodItemId);
        }

        int nutritionBefore = pet.Nutrition;

        pet.X = food.X;
        pet.Y = food.Y;
        pet.Z = food.Z;
        pet.Direction = (int)food.Rotation;
        pet.Nutrition = Math.Min(pet.Nutrition + petFood.Nutrition, nutritionCap);
        pet.Energy = Math.Min(pet.Energy + petFood.Energy, energyCap);
        // Water slakes thirst and nothing else. It used to top up energy, which is why a pet that
        // drank stopped wanting a nap.
        pet.Thirst = Math.Min(pet.Thirst + petFood.Thirst, ThirstCap);

        int currentUses = int.TryParse(food.ExtraData, out int parsed) ? parsed : petFood.MaxUses;
        int usesRemaining = Math.Max(0, currentUses - 1);

        if (usesRemaining == 0)
        {
            food.RoomEntityId = null;
            food.DeletedAt = DateTime.UtcNow;
        }
        else
        {
            food.ExtraData = usesRemaining.ToString();
        }

        await dbCtx.SaveChangesAsync(ct);

        return new PetFeedResult
        {
            Success = true,
            Pet = ToSnapshot(pet),
            FoodItemId = foodItemId,
            NutritionAdded = petFood.Nutrition,
            NutritionBefore = nutritionBefore,
            NutritionAfter = pet.Nutrition,
            UsesRemaining = usesRemaining,
            FoodState = usesRemaining,
        };
    }
}

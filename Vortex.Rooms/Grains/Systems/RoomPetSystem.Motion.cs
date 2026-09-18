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
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Protocol.Messages.Outgoing.Inventory.Pets;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Room.Pets;
using Vortex.Protocol.Messages.Outgoing.Users;
using Vortex.Rooms.Object.Logic.Furniture.Floor;

namespace Vortex.Rooms.Grains.Systems;

public sealed partial class RoomPetSystem
{
    public async Task<PetFeedResult> FeedPetAsync(
        ActionContext ctx,
        int petId,
        RoomObjectId foodItemId,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _roomGrain._dbCtxFactory.CreateDbContextAsync(ct);

        // Sync live in-memory stats into this context so the feed operates on the correct base
        await SyncLiveStatsToPetEntityAsync(dbCtx, petId, ct);

        PetFeedResult result = await RoomPetRuntime.FeedAsync(
            dbCtx,
            _roomGrain.RoomId.Value,
            ctx.PlayerId.Value,
            petId,
            foodItemId,
            _roomGrain._state.RoomSnapshot.AllowPetsEat,
            _roomGrain._roomConfig.Pet.NutritionCap,
            _roomGrain._roomConfig.Pet.EnergyCap,
            ct
        );

        if (!result.Success || result.Pet is null)
        {
            return result;
        }

        _roomGrain._state.PetsById[petId] = result.Pet;

        if (_motionByPetId.TryGetValue(petId, out PetMotionState? feedMotion))
        {
            feedMotion.IsStatsDirty = false;
        }

        await SendPetUpdatedAsync(result.Pet, ct);
        await UpdateFoodItemInLiveStateAsync(
            ctx,
            foodItemId,
            result.UsesRemaining,
            result.FoodState,
            ct
        );

        return result;
    }

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

            if (motion.FeedTargetId is RoomObjectId feedId)
            {
                motion.FeedTargetId = null;
                bool ate = await AutoFeedPetAtBowlAsync(pet, feedId, ct);
                PetSnapshot petAfterFeed = _roomGrain._state.PetsById.TryGetValue(
                    pet.PetId,
                    out PetSnapshot? fed
                )
                    ? fed
                    : pet;

                if (ate)
                {
                    return await ToAvatarSnapshotAsync(
                        petAfterFeed,
                        RoomPetRuntime.EatStatus(petAfterFeed.Z),
                        RoomPetRuntime.EatPosture,
                        ct
                    );
                }

                return await ToAvatarSnapshotAsync(petAfterFeed, ct);
            }

            if (motion.IsHeadingToNest)
            {
                motion.IsHeadingToNest = false;

                // Only curl up if the walk actually ended on the nest -- a path cut short by a
                // blocked tile leaves the pet standing wherever it stopped, and it should retry.
                if (IsOnNestTile(pet))
                {
                    StartNestNap(motion, now);
                }
            }
            else if (motion.IsHeadingToToy)
            {
                motion.IsHeadingToToy = false;

                RoomPetAvatarSnapshot? played = await StartToyPlayAsync(pet, motion, now, ct);

                if (played is not null)
                {
                    return played;
                }
            }

            return await ToAvatarSnapshotAsync(pet, ct);
        }

        if (motion.TilePath.Count == 0 && now >= motion.NextWanderAtMs)
        {
            // Needs first, then sleep, then boredom: a starving pet should not stop to play.
            if (
                !TryDirectPetToFood(pet, motion, now)
                && !TryDirectPetToNest(pet, motion, now)
                && !TryDirectPetToToy(pet, motion, now)
            )
            {
                TryStartWander(pet, motion, now);
            }
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
        if (pet.Type == MonsterplantPetType)
        {
            return false;
        }

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

            int targetTileId = _roomGrain.MapModule.ToIdx(targetX, targetY);

            if (!CanPetOccupyTile(pet.PetId, targetTileId))
            {
                continue;
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
                continue;
            }

            motion.TilePath.Clear();
            motion.TilePath.AddRange(
                path.Skip(1).Select(pos => _roomGrain.MapModule.ToIdx(pos.X, pos.Y))
            );

            return true;
        }

        return false;
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

    private PetSnapshot ApplyNeedDecay(PetSnapshot pet, PetMotionState motion, long now)
    {
        int nutritionCap = _roomGrain._petLevelProvider.GetNutritionCapForLevel(
            pet.Type,
            pet.Level
        );
        int energyCap = _roomGrain._petLevelProvider.GetEnergyCapForLevel(pet.Type, pet.Level);

        int newNutrition = pet.Nutrition;
        int newEnergy = pet.Energy;

        int nutritionLoss = RoomPetRuntime.TakeWholeNeedPoints(
            motion.LastNutritionDecayAtMs,
            now,
            Tuning.NutritionDecayPerMinute,
            out long nextNutritionClockMs
        );
        motion.LastNutritionDecayAtMs = nextNutritionClockMs;

        if (nutritionLoss > 0)
        {
            newNutrition = Math.Clamp(pet.Nutrition - nutritionLoss, 0, nutritionCap);
        }

        if (motion.IsSleeping)
        {
            double nestMultiplier = IsOnNestTile(pet)
                ? _roomGrain._roomConfig.Pet.NestEnergyMultiplier
                : 1.0;
            int energyGain = RoomPetRuntime.TakeWholeNeedPoints(
                motion.LastEnergyDecayAtMs,
                now,
                Tuning.EnergyDecayPerMinute * 2 * nestMultiplier,
                out long nextEnergyClockMs
            );
            motion.LastEnergyDecayAtMs = nextEnergyClockMs;

            if (energyGain > 0)
            {
                newEnergy = Math.Clamp(pet.Energy + energyGain, 0, energyCap);
            }

            if (newEnergy >= Tuning.SleepWakeEnergyThreshold)
            {
                motion.IsSleeping = false;
                motion.SleepPostureSent = false;
                motion.PendingWakeVocal = true;
            }
        }
        else
        {
            int energyLoss = RoomPetRuntime.TakeWholeNeedPoints(
                motion.LastEnergyDecayAtMs,
                now,
                Tuning.EnergyDecayPerMinute,
                out long nextEnergyClockMs
            );
            motion.LastEnergyDecayAtMs = nextEnergyClockMs;

            if (energyLoss > 0)
            {
                newEnergy = Math.Clamp(pet.Energy - energyLoss, 0, energyCap);
            }

            if (newEnergy == 0)
            {
                motion.IsSleeping = true;
                motion.SleepPostureSent = false;
                motion.PendingSleepVocal = true;
                motion.ClearMovement();
            }
        }

        // Water is its own need, on its own clock. It used to be read off energy, which meant one
        // bar answered both "wants a drink" and "wants a nap", and a pet that drank stopped being
        // sleepy. Habbo counts the two apart.
        int thirstLoss = RoomPetRuntime.TakeWholeNeedPoints(
            motion.LastThirstDecayAtMs,
            now,
            Tuning.ThirstDecayPerMinute,
            out long nextThirstClockMs
        );
        motion.LastThirstDecayAtMs = nextThirstClockMs;

        int newThirst = thirstLoss > 0 ? Math.Clamp(pet.Thirst - thirstLoss, 0, 100) : pet.Thirst;

        // Mood runs on its own clock too, draining while the pet is up and paying back while it
        // rests, so a pet left alone slowly sulks and a nap cheers it up.
        int happinessCap = _roomGrain._roomConfig.Pet.HappinessCap;
        int happinessStep = RoomPetRuntime.TakeWholeNeedPoints(
            motion.LastHappinessDecayAtMs,
            now,
            motion.IsSleeping ? Tuning.HappinessRestGainPerMinute : Tuning.HappinessDecayPerMinute,
            out long nextHappinessClockMs
        );
        motion.LastHappinessDecayAtMs = nextHappinessClockMs;

        int newHappiness = pet.Happiness;

        if (happinessStep > 0)
        {
            newHappiness = Math.Clamp(
                motion.IsSleeping ? pet.Happiness + happinessStep : pet.Happiness - happinessStep,
                0,
                happinessCap
            );
        }

        if (
            newNutrition == pet.Nutrition
            && newEnergy == pet.Energy
            && newHappiness == pet.Happiness
            && newThirst == pet.Thirst
        )
        {
            return pet;
        }

        motion.IsStatsDirty = true;

        PetSnapshot updated = pet with
        {
            Nutrition = newNutrition,
            Energy = newEnergy,
            Happiness = newHappiness,
            Thirst = newThirst,
        };
        _roomGrain._state.PetsById[pet.PetId] = updated;

        return updated;
    }

    private void ApplyOfflineDecay(PetEntity entity, long nowMs)
    {
        long entityUpdatedMs = new DateTimeOffset(
            DateTime.SpecifyKind(entity.UpdatedAt, DateTimeKind.Utc)
        ).ToUnixTimeMilliseconds();

        long elapsedMs = Math.Max(0L, nowMs - entityUpdatedMs);

        if (elapsedMs <= 0)
        {
            return;
        }

        double elapsedMinutes = elapsedMs / 60_000.0;
        entity.Nutrition = Math.Max(
            0,
            entity.Nutrition - (int)(elapsedMinutes * Tuning.NutritionDecayPerMinute)
        );
        entity.Energy = Math.Max(
            0,
            entity.Energy - (int)(elapsedMinutes * Tuning.EnergyDecayPerMinute)
        );
        entity.Thirst = Math.Max(
            0,
            entity.Thirst - (int)(elapsedMinutes * Tuning.ThirstDecayPerMinute)
        );
        // Mood ages with the rest of it, or a pet left for a week comes back starving and delighted.
        entity.Happiness = Math.Max(
            0,
            entity.Happiness - (int)(elapsedMinutes * Tuning.HappinessDecayPerMinute)
        );
    }

    private async Task SyncLiveStatsToPetEntityAsync(
        VortexDbContext dbCtx,
        int petId,
        CancellationToken ct
    )
    {
        if (!_roomGrain._state.PetsById.TryGetValue(petId, out PetSnapshot? live))
        {
            return;
        }

        PetEntity? entity = await dbCtx.Pets.SingleOrDefaultAsync(
            p => p.Id == petId && p.DeletedAt == null,
            ct
        );

        if (entity is null)
        {
            return;
        }

        entity.Nutrition = live.Nutrition;
        entity.Energy = live.Energy;
        entity.Experience = live.Experience;
        entity.Level = live.Level;
        entity.Respect = live.Respect;
        entity.Happiness = live.Happiness;
        entity.Thirst = live.Thirst;
        entity.CanBreed = live.CanBreed;
    }

    private bool IsOnNestTile(PetSnapshot pet)
    {
        foreach (IRoomItem item in _roomGrain._state.ItemsById.Values)
        {
            if (item.X == pet.X && item.Y == pet.Y && item.Logic is FurniturePetNestLogic)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A tired pet walks to the nearest reachable nest and naps there.
    /// </summary>
    /// <remarks>
    /// Nothing used to take a pet to its nest. It slept where it stood, only once energy had hit
    /// zero, and <see cref="IsOnNestTile" />'s recovery bonus applied only if it happened to be
    /// standing on a nest at that moment.
    /// </remarks>
    private bool TryDirectPetToNest(
        PetSnapshot pet,
        PetMotionState motion,
        long now,
        bool ordered = false
    )
    {
        // An ordered pet goes whether or not it feels like it -- that is what being told means.
        if (!ordered && !RoomPetRuntime.IsTired(pet, Tuning.TiredEnergyThreshold))
        {
            return false;
        }

        if (IsOnNestTile(pet))
        {
            StartNestNap(motion, now);

            return true;
        }

        (int X, int Y)? nest = RoomPetRuntime.PickNearestTile(
            pet.X,
            pet.Y,
            _roomGrain
                ._state.ItemsById.Values.Where(item => item.Logic is FurniturePetNestLogic)
                .Select(item => (item.X, item.Y))
        );

        if (nest is null)
        {
            return false;
        }

        (int nestX, int nestY) = nest.Value;

        IReadOnlyList<(int X, int Y)> path = _roomGrain.PathingSystem.FindPath(
            (pet.X, pet.Y),
            (nestX, nestY),
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
        motion.IsHeadingToNest = true;
        motion.NextWanderAtMs = ScheduleNextWanderAt(now);

        return true;
    }

    /// <summary>
    /// A bored pet goes and plays with the nearest toy. Habbo's own guides name toys as what cheers
    /// a pet up; nothing in the hotel had ever driven one, because no toy furni carried a logic.
    /// </summary>
    private bool TryDirectPetToToy(PetSnapshot pet, PetMotionState motion, long now)
    {
        if (!CanPlayWithAToy(pet, motion, now))
        {
            return false;
        }

        // Boredom is the reason it must go; the whim is why it sometimes goes anyway. A pet that
        // only ever played when miserable would look like a machine.
        bool bored = RoomPetRuntime.IsBored(pet, Tuning.BoredHappinessThreshold);
        bool onAWhim = Random.Shared.Next(100) < Tuning.ToyPlayChancePercent;

        if (!bored && !onAWhim)
        {
            return false;
        }

        (int X, int Y)? toy = RoomPetRuntime.PickNearestTile(
            pet.X,
            pet.Y,
            _roomGrain
                ._state.ItemsById.Values.Where(item => item.Logic is FurniturePetToyLogic)
                .Select(item => (item.X, item.Y))
        );

        if (toy is null)
        {
            return false;
        }

        (int toyX, int toyY) = toy.Value;

        if (pet.X == toyX && pet.Y == toyY)
        {
            // Already standing on it -- play where it is rather than pathing nowhere.
            motion.IsHeadingToToy = true;
            motion.PendingStopAtMs = _roomGrain.AlignToNextBoundary(
                now,
                _roomGrain._roomConfig.Pet.TickMs
            );

            return true;
        }

        IReadOnlyList<(int X, int Y)> path = _roomGrain.PathingSystem.FindPath(
            (pet.X, pet.Y),
            (toyX, toyY),
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
        motion.IsHeadingToToy = true;
        motion.NextWanderAtMs = ScheduleNextWanderAt(now);

        return true;
    }

    /// <summary>
    /// Whether a pet is in any state to play: not exhausted, and not still inside the cooldown that
    /// stops a pet pacing across a ball from topping its mood up for free.
    /// </summary>
    private bool CanPlayWithAToy(PetSnapshot pet, PetMotionState motion, long now) =>
        pet.Type != MonsterplantPetType
        && pet.Energy > Tuning.PlayEnergyThreshold
        && now >= motion.NextToyPlayAtMs
        && motion.PlayingWithToyId is null;

    /// <summary>
    /// Starts a bout of play on the toy under the pet, whatever brought it there -- it may have set
    /// out for the toy, or simply wandered across one.
    /// </summary>
    private async Task<RoomPetAvatarSnapshot?> StartToyPlayAsync(
        PetSnapshot pet,
        PetMotionState motion,
        long now,
        CancellationToken ct
    )
    {
        IRoomItem? toy = _roomGrain._state.ItemsById.Values.FirstOrDefault(item =>
            IsToyUnder(item, pet)
        );

        if (toy is null || !CanPlayWithAToy(pet, motion, now))
        {
            return null;
        }

        PetSnapshot updated = pet with
        {
            Happiness = Math.Clamp(
                pet.Happiness + Tuning.ToyHappinessReward,
                0,
                _roomGrain._roomConfig.Pet.HappinessCap
            ),
        };
        _roomGrain._state.PetsById[pet.PetId] = updated;

        motion.ClearMovement();
        motion.IsStatsDirty = true;
        motion.PlayingWithToyId = toy.ObjectId;
        motion.ToyPlayEndsAtMs = now + Tuning.ToyPlayDurationMs;
        motion.NextToyPlayAtMs = motion.ToyPlayEndsAtMs + Tuning.ToyPlayCooldownMs;
        motion.NextWanderAtMs = motion.ToyPlayEndsAtMs;

        await SetToyInUseAsync(toy, inUse: true);
        await BroadcastPetVocalAsync(updated, "PLAYFUL");

        return await ToAvatarSnapshotAsync(
            updated,
            RoomPetRuntime.PlayStatus(updated.Z),
            RoomPetRuntime.PlayPosture,
            ct
        );
    }

    /// <summary>Ends the bout: the toy goes back to its resting frame and the pet gets up.</summary>
    private async Task<RoomPetAvatarSnapshot?> FinishToyPlayAsync(
        PetSnapshot pet,
        PetMotionState motion,
        CancellationToken ct
    )
    {
        if (
            motion.PlayingWithToyId is not RoomObjectId toyId
            || !_roomGrain._state.ItemsById.TryGetValue(toyId, out IRoomItem? toy)
        )
        {
            motion.PlayingWithToyId = null;

            return null;
        }

        motion.PlayingWithToyId = null;
        await SetToyInUseAsync(toy, inUse: false);

        return await ToAvatarSnapshotAsync(pet, ct);
    }

    /// <summary>
    /// Paints the toy. Arcturus flips the same extra data to "1" while a pet is on it and back to
    /// "0" afterwards, which is what animates the furni.
    /// </summary>
    private Task SetToyInUseAsync(IRoomItem toy, bool inUse)
    {
        string state = inUse ? "1" : "0";

        toy.Logic.StuffData.SetState(state);
        toy.SetExtraData(state);

        return _roomGrain.SendComposerToRoomAsync(toy.GetRefreshStuffDataComposer());
    }

    private static bool IsToyUnder(IRoomItem item, PetSnapshot pet) =>
        item.X == pet.X && item.Y == pet.Y && item.Logic is FurniturePetToyLogic;

    private void StartNestNap(PetMotionState motion, long now)
    {
        motion.ClearMovement();
        motion.IsSleeping = true;
        motion.SleepPostureSent = false;
        motion.PendingSleepVocal = true;
        motion.NextWanderAtMs = ScheduleNextWanderAt(now);
    }

    private bool TryDirectPetToFood(
        PetSnapshot pet,
        PetMotionState motion,
        long now,
        bool ordered = false
    )
    {
        if (pet.Type == MonsterplantPetType || !_roomGrain._state.RoomSnapshot.AllowPetsEat)
        {
            return false;
        }

        bool needsFood = ordered || pet.Nutrition < Tuning.HungerThreshold;
        bool needsDrink = ordered || pet.Thirst < Tuning.ThirstThreshold;

        if (!needsFood && !needsDrink)
        {
            return false;
        }

        IRoomItem? target = null;
        int bestDist = int.MaxValue;

        foreach (IRoomItem item in _roomGrain._state.ItemsById.Values)
        {
            bool isFood = needsFood && item.Logic is FurniturePetProductLogic;
            bool isDrink = needsDrink && item.Logic is FurniturePetDrinkLogic;

            if (!isFood && !isDrink)
            {
                continue;
            }

            if (!RoomPetRuntime.HasServingsLeft(item.Definition.TotalStates, item.Logic.GetState()))
            {
                continue;
            }

            int dist = Math.Abs(pet.X - item.X) + Math.Abs(pet.Y - item.Y);

            if (dist < bestDist)
            {
                bestDist = dist;
                target = item;
            }
        }

        if (target is null)
        {
            return false;
        }

        IReadOnlyList<(int X, int Y)> path = _roomGrain.PathingSystem.FindPath(
            (pet.X, pet.Y),
            (target.X, target.Y),
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
        motion.FeedTargetId = target.ObjectId;
        motion.NextWanderAtMs = ScheduleNextWanderAt(now);

        return true;
    }

    private async Task<bool> AutoFeedPetAtBowlAsync(
        PetSnapshot pet,
        RoomObjectId feedItemId,
        CancellationToken ct
    )
    {
        if (!_roomGrain._state.ItemsById.TryGetValue(feedItemId, out IRoomItem? item))
        {
            return false;
        }

        if (item.X != pet.X || item.Y != pet.Y)
        {
            return false;
        }

        bool isDrink = item.Logic is FurniturePetDrinkLogic;

        ActionContext ctx = ActionContext.CreateForPlayer(pet.OwnerId, _roomGrain.RoomId);
        PetFeedResult result = await FeedPetAsync(ctx, pet.PetId, feedItemId, ct);

        if (!result.Success)
        {
            return false;
        }

        if (_roomGrain._state.PetsById.TryGetValue(pet.PetId, out PetSnapshot? updated))
        {
            // The vocal still tells food from water even though the posture cannot: a pet has one
            // animation for both.
            string eatVocal = isDrink ? "DRINKING" : "EATING";
            await BroadcastPetVocalAsync(updated, eatVocal);
        }

        return true;
    }

    private async Task UpdateFoodItemInLiveStateAsync(
        ActionContext ctx,
        RoomObjectId foodItemId,
        int usesRemaining,
        int foodState,
        CancellationToken ct
    )
    {
        if (!_roomGrain._state.ItemsById.TryGetValue(foodItemId, out IRoomItem? item))
        {
            return;
        }

        if (usesRemaining > 0)
        {
            item.Logic.StuffData.SetState(foodState.ToString());
            item.SetExtraData(foodState.ToString());
            await _roomGrain.SendComposerToRoomAsync(item.GetRefreshStuffDataComposer());
            return;
        }

        if (!_roomGrain.MapModule.RemoveItem(item))
        {
            return;
        }

        await _roomGrain.SendComposerToRoomAsync(item.GetRemoveComposer(ctx.PlayerId, true));

        await item.Logic.OnDetachAsync(ct);

        _roomGrain.ObjectModule.DetachItemFromLiveState(item);

        // The row was marked deleted inside the feed transaction, not here — but this is where the
        // last use is spent, so this is where the deletion is announced.
        await _roomGrain._events.PublishAsync(
            new ItemDeletedEvent(
                foodItemId.Value,
                item.OwnerId.Value,
                ctx.PlayerId == PlayerId.Invalid ? null : ctx.PlayerId.Value,
                ItemDeletionReason.PetFoodUsedUp
            ),
            ct
        );
    }
}

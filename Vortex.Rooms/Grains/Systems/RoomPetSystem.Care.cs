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
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Protocol.Messages.Outgoing.Inventory.Pets;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Protocol.Messages.Outgoing.Room.Chat;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Room.Pets;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.Rooms.Grains.Systems;

public sealed partial class RoomPetSystem
{
    public async Task<PetSnapshot?> RespectPetAsync(
        ActionContext ctx,
        int petId,
        CancellationToken ct
    )
    {
        await EnsurePetsLoadedAsync(ct);

        if (!_roomGrain._state.PetsById.TryGetValue(petId, out PetSnapshot? pet))
        {
            return null;
        }

        // One packet, two buttons: the client sends the same composer for "respect" on an animal and
        // "treat" on a monsterplant, so the pet's type is what tells them apart. A plant is watered,
        // and none of the respect rules below (daily cap, account age) apply to it.
        if (RoomPetRuntime.IsPlant(pet))
        {
            return await TreatPlantAsync(ctx, petId, ct);
        }

        int minimumAgeDays = _roomGrain._roomConfig.Pet.RespectMinimumAccountAgeDays;

        if (minimumAgeDays > 0)
        {
            PlayerSummarySnapshot summary = await _roomGrain
                ._grainFactory.GetPlayerGrain(ctx.PlayerId)
                .GetSummaryAsync(ct);

            int ageDays = (int)(DateTime.UtcNow - summary.CreatedAt).TotalDays;

            if (ageDays < minimumAgeDays)
            {
                // The client shows both numbers, so it needs both -- this used to send nothing and
                // the refusal was silent.
                await _roomGrain
                    ._grainFactory.GetPlayerPresenceGrain(ctx.PlayerId)
                    .SendComposerAsync(
                        new PetRespectFailedMessageComposer
                        {
                            RequiredDays = minimumAgeDays,
                            AvatarAgeInDays = ageDays,
                        }
                    );

                return pet;
            }
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (pet.RespectLastResetDate != today)
        {
            pet = pet with { RespectTodayCount = 0, RespectLastResetDate = today };
            _roomGrain._state.PetsById[petId] = pet;
        }

        int dailyCap = _roomGrain._roomConfig.Pet.RespectDailyCapPerPet;

        if (pet.RespectTodayCount >= dailyCap)
        {
            return pet;
        }

        int respectXp = _roomGrain._roomConfig.Pet.RespectXpReward;
        PetSnapshot updated = await GrantXpAndLevelUpAsync(pet, respectXp, ct);
        updated = updated with
        {
            Respect = updated.Respect + 1,
            RespectTodayCount = updated.RespectTodayCount + 1,
            RespectLastResetDate = today,
        };
        _roomGrain._state.PetsById[petId] = updated;

        if (_motionByPetId.TryGetValue(petId, out PetMotionState? motion))
        {
            motion.IsStatsDirty = true;
        }

        await SendPetUpdatedAsync(updated, ct);
        await BroadcastPetRespectNotificationAsync(updated);

        // Detached, like the furniture events: the scratcher's progression must not sit between
        // the click and the animation. Both sides ride one event -- the hotel counts scratching
        // somebody's pet and having yours scratched as two different achievements.
        _roomGrain.PublishDetached(new PetRespectedEvent(ctx.PlayerId, updated.OwnerId, petId));

        return updated;
    }

    public async Task<PetSnapshot?> GrantPetCommandXpAsync(
        ActionContext ctx,
        int petId,
        CancellationToken ct
    )
    {
        await EnsurePetsLoadedAsync(ct);

        if (!_roomGrain._state.PetsById.TryGetValue(petId, out PetSnapshot? pet))
        {
            return null;
        }

        int commandXp = _roomGrain._roomConfig.Pet.CommandXpReward;
        PetSnapshot updated = await GrantXpAndLevelUpAsync(pet, commandXp, ct);
        _roomGrain._state.PetsById[petId] = updated;

        if (_motionByPetId.TryGetValue(petId, out PetMotionState? motion))
        {
            motion.IsStatsDirty = true;
        }

        await SendPetUpdatedAsync(updated, ct);

        return updated;
    }

    public async Task<PetSnapshot?> GiveSupplementToPetAsync(
        ActionContext ctx,
        int petId,
        CancellationToken ct
    )
    {
        await EnsurePetsLoadedAsync(ct);

        if (!_roomGrain._state.PetsById.TryGetValue(petId, out PetSnapshot? pet))
        {
            return null;
        }

        int energyCap = _roomGrain._petLevelProvider.GetEnergyCapForLevel(pet.Type, pet.Level);
        int newEnergy = Math.Min(
            pet.Energy + _roomGrain._roomConfig.Pet.SupplementEnergyBoost,
            energyCap
        );

        int supplementXp = _roomGrain._roomConfig.Pet.SupplementXpReward;
        PetSnapshot withEnergy = pet with { Energy = newEnergy };
        _roomGrain._state.PetsById[petId] = withEnergy;

        PetSnapshot updated = await GrantXpAndLevelUpAsync(withEnergy, supplementXp, ct);
        _roomGrain._state.PetsById[petId] = updated;

        bool petWokeUp = false;

        if (_motionByPetId.TryGetValue(petId, out PetMotionState? motion))
        {
            motion.IsStatsDirty = true;
            if (motion.IsSleeping && updated.Energy >= Tuning.SleepWakeEnergyThreshold)
            {
                motion.IsSleeping = false;
                motion.SleepPostureSent = false;
                petWokeUp = true;
            }
        }

        await SendPetUpdatedAsync(updated, ct);

        if (petWokeUp)
        {
            await BroadcastPetVocalAsync(updated, "GENERIC_HAPPY");
        }

        return updated;
    }

    /// <summary>
    /// A pet says its piece. There is no pet-speech message on the wire: the client has no parser
    /// for one, and nothing in it resolves the <c>pet.vocals.*</c> keys the text bundle is written
    /// in. So the server picks the line and the pet speaks as any other room object does.
    /// </summary>
    private Task BroadcastPetVocalAsync(PetSnapshot pet, string vocalType)
    {
        string? line = _roomGrain._petVocalProvider.TryGetLine(pet.Type, vocalType);

        // A pet type with no dialogue written for it stays quiet.
        if (string.IsNullOrEmpty(line))
        {
            return Task.CompletedTask;
        }

        return _roomGrain.SendComposerToRoomAsync(
            new ChatMessageComposer
            {
                ObjectId = RoomPetRuntime.ToRoomObjectId(pet.PetId),
                Text = line,
                Gesture = default,
                StyleId = 0,
                Links = [],
                TrackingId = 0,
            }
        );
    }

    private Task BroadcastPetRespectNotificationAsync(PetSnapshot pet)
    {
        return _roomGrain.SendComposerToRoomAsync(
            new PetRespectNotificationEventMessageComposer
            {
                PetRespect = pet.Respect,
                PetOwnerId = pet.OwnerId.Value,
                PetId = pet.PetId,
                PetName = pet.Name,
                PetType = pet.Type,
                PetColor = pet.Color,
                PetRace = pet.Race,
                PetLevel = pet.Level,
                PetRarityLevel = pet.RarityLevel,
            }
        );
    }

    private string SelectVocalForState(PetSnapshot pet, PetMotionState motion)
    {
        if (motion.IsSleeping)
        {
            return "SLEEPING";
        }

        bool isHungry = pet.Nutrition < Tuning.HungerThreshold;
        bool isThirsty = pet.Thirst < Tuning.ThirstThreshold;
        bool isTired = pet.Energy > 0 && pet.Energy <= Tuning.TiredEnergyThreshold;

        if (isHungry)
        {
            return "HUNGRY";
        }

        if (isThirsty)
        {
            return "THIRSTY";
        }

        if (isTired)
        {
            return "TIRED";
        }

        return Random.Shared.Next(0, 3) switch
        {
            0 => "GENERIC_NEUTRAL",
            1 => "GENERIC_HAPPY",
            _ => "PLAYFUL",
        };
    }

    public async Task<PetSnapshot?> IssueCommandAsync(
        ActionContext ctx,
        int petId,
        int commandId,
        CancellationToken ct
    )
    {
        await EnsurePetsLoadedAsync(ct);

        if (!_roomGrain._state.PetsById.TryGetValue(petId, out PetSnapshot? pet))
        {
            return null;
        }

        if (pet.OwnerId != ctx.PlayerId)
        {
            return null;
        }

        PetCommandEntry? cmd = _roomGrain._petCommandProvider.GetCommandConfig(pet.Type, commandId);

        if (cmd is null)
        {
            return null;
        }

        if (pet.Level < cmd.LevelRequired)
        {
            await BroadcastPetVocalAsync(pet, "UNKNOWN_COMMAND");
            return null;
        }

        if (_motionByPetId.TryGetValue(petId, out PetMotionState? motion) && motion.IsSleeping)
        {
            await BroadcastPetVocalAsync(pet, "SLEEPING");
            return null;
        }

        if (pet.Energy < cmd.EnergyCost)
        {
            await BroadcastPetVocalAsync(pet, "TIRED");
            return null;
        }

        int newEnergy = pet.Energy - cmd.EnergyCost;

        // Obeying pleases it. This is the only thing that lifts mood other than resting.
        int newHappiness = Math.Clamp(
            pet.Happiness + Tuning.CommandHappinessReward,
            0,
            _roomGrain._roomConfig.Pet.HappinessCap
        );
        PetSnapshot withEnergy = pet with { Energy = newEnergy, Happiness = newHappiness };
        _roomGrain._state.PetsById[petId] = withEnergy;

        PetSnapshot updated = await GrantXpAndLevelUpAsync(withEnergy, cmd.XpReward, ct);
        _roomGrain._state.PetsById[petId] = updated;

        if (motion is not null)
        {
            motion.IsStatsDirty = true;

            if (newEnergy == 0)
            {
                motion.IsSleeping = true;
                motion.SleepPostureSent = false;
            }
        }

        // Two commands are errands, not poses: the pet has to walk somewhere for them to mean
        // anything. Ordered, so it goes whether or not it is tired or hungry enough to have gone on
        // its own.
        if (motion is not null)
        {
            if (commandId == RoomPetRuntime.NestCommandId)
            {
                TryDirectPetToNest(updated, motion, _roomGrain.NowMs(), ordered: true);
            }
            else if (commandId == RoomPetRuntime.EatCommandId)
            {
                TryDirectPetToFood(updated, motion, _roomGrain.NowMs(), ordered: true);
            }
        }

        if (!string.IsNullOrEmpty(cmd.Posture))
        {
            // With the height, or the client drops the segment and the trick is never seen.
            RoomPetAvatarSnapshot postureSnapshot = await ToAvatarSnapshotAsync(
                updated,
                RoomPetRuntime.StatusWithHeight(cmd.Posture, updated.Z),
                cmd.Posture,
                ct
            );

            await _roomGrain.SendComposerToRoomAsync(
                new UserUpdateMessageComposer { Avatars = [postureSnapshot] }
            );
        }
        else
        {
            await SendPetUpdatedAsync(updated, ct);
        }

        return updated;
    }

    private async Task<PetSnapshot> GrantXpAndLevelUpAsync(
        PetSnapshot pet,
        int xp,
        CancellationToken ct
    )
    {
        int newExperience = pet.Experience + xp;
        int newLevel = _roomGrain._petLevelProvider.GetLevelForExperience(pet.Type, newExperience);
        bool leveledUp = newLevel > pet.Level;
        int maxLevel = _roomGrain._petLevelProvider.GetMaxLevel(pet.Type);

        PetSnapshot updated = pet with { Experience = newExperience, Level = newLevel };
        _roomGrain._state.PetsById[pet.PetId] = updated;

        // Only the floating "+N" over the pet. The totals and thresholds this used to broadcast
        // belong to PetInfo, which the client asks for when the pet's dialog is opened.
        await _roomGrain.SendComposerToRoomAsync(
            new PetExperienceMessageComposer
            {
                PetId = updated.PetId,
                PetRoomIndex = RoomPetRuntime.ToRoomObjectId(updated.PetId).Value,
                GainedExperience = xp,
            }
        );

        if (leveledUp)
        {
            await _roomGrain._events.PublishAsync(
                new PetLeveledUpEvent(
                    updated.OwnerId,
                    updated.PetId,
                    _roomGrain.RoomId.Value,
                    updated.Level
                ),
                ct
            );

            await _roomGrain.SendComposerToRoomAsync(
                new PetLevelUpdateMessageComposer
                {
                    RoomIndex = RoomPetRuntime.ToRoomObjectId(updated.PetId).Value,
                    PetId = updated.PetId,
                    Level = updated.Level,
                }
            );

            await _roomGrain.SendComposerToRoomAsync(
                new UserUpdateMessageComposer
                {
                    Avatars = [await ToAvatarSnapshotAsync(updated, ct)],
                }
            );

            await BroadcastPetVocalAsync(updated, "LEVEL_UP");

            try
            {
                await _roomGrain
                    ._grainFactory.GetPlayerPresenceGrain(updated.OwnerId)
                    .SendComposerAsync(
                        new PetLevelNotificationEventMessageComposer
                        {
                            PetId = updated.PetId,
                            NewLevel = updated.Level,
                            PetName = updated.Name,
                            Pet = updated,
                        }
                    );
            }
            catch (Exception ex)
            {
                _roomGrain._logger.LogError(
                    ex,
                    "Failed to send pet level-up notification for pet {PetId}",
                    updated.PetId
                );
            }
        }

        return updated;
    }
}

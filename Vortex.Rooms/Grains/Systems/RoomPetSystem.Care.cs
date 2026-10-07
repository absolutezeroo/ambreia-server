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
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Protocol.Messages.Outgoing.Inventory.Pets;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Protocol.Messages.Outgoing.Room.Action;
using Vortex.Protocol.Messages.Outgoing.Room.Chat;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Room.Pets;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.Rooms.Grains.Systems;

public sealed partial class RoomPetSystem
{
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
                Links = [],
                TrackingId = 0,
            }
        );
    }

    private string SelectVocalForState(PetSnapshot pet, PetMotionState motion)
    {
        if (motion.IsSleeping)
        {
            return "SLEEPING";
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

        // A pet is allowed to say no. Refusing is free -- it pays no experience, which is what the
        // capture shows: the order that was ignored moved nothing.
        int obeyChance = RoomPetRuntime.ObeyChancePercent(
            pet.Level,
            cmd.LevelRequired,
            Tuning.CommandObeyBaseChancePercent,
            Tuning.CommandObeyChancePerLevelPercent,
            Tuning.CommandObeyMaxChancePercent
        );

        if (Random.Shared.Next(100) >= obeyChance)
        {
            await BroadcastPetVocalAsync(pet, "DISOBEY");
            return null;
        }

        // Obeying pays nothing by default -- the capture caught a pet obeying three orders with
        // its happiness pinned. The knob stays for a hotel that wants it to.
        int newHappiness = Math.Clamp(
            pet.Happiness + Tuning.CommandHappinessReward,
            0,
            _roomGrain._roomConfig.Pet.HappinessCap
        );
        PetSnapshot withHappiness = pet with { Happiness = newHappiness };
        _roomGrain._state.PetsById[petId] = withHappiness;

        PetSnapshot updated = await GrantXpAndLevelUpAsync(withHappiness, cmd.XpReward, ct);
        _roomGrain._state.PetsById[petId] = updated;

        if (motion is not null)
        {
            motion.IsStatsDirty = true;

            // The nest is an errand, not a pose: the pet has to walk there for it to mean anything.
            if (commandId == RoomPetRuntime.NestCommandId)
            {
                TryDirectPetToNest(updated, motion, _roomGrain.NowMs());
            }
        }

        if (!string.IsNullOrEmpty(cmd.Posture))
        {
            // With the height, or the client drops the segment and the trick is never seen.
            RoomPetAvatarSnapshot postureSnapshot = await ToAvatarSnapshotAsync(
                updated,
                PetAnimation.StatusWithHeight(cmd.Posture, updated.Z),
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

        // Detached, like the scratch: the owner's progression must not sit between the order and
        // the pet doing the trick. Here at the end rather than at the top of the method, so nothing
        // is counted for an order a sleeping, under-levelled or somebody else's pet refused.
        _roomGrain.PublishDetached(
            new PetCommandObeyedEvent(ctx.PlayerId, updated.OwnerId, petId, updated.Type, commandId)
        );

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
                    updated.Type,
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

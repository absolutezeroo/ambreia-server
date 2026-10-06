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

        // You cannot scratch a pet from across the room: the hotel walks you up to it first and the
        // scratch lands when you get there. The client sends nothing but the pet id, so the approach
        // is the server's -- the official capture of a scratch carries no walk at all, because the
        // account that recorded it was already standing next to the pet.
        //
        // The reference emulator does the same (RespectPetMessageEvent: getClosestAdjacentTile then
        // walk, with the scratch queued as the arrival task), and it is the behaviour Krews track as
        // #1886, "when scratching a pet you should walk to an adjacent tile before you actually
        // scratch the pet".
        if (await TryApproachPetAsync(ctx, pet, c => ApplyRespectAsync(ctx, petId, c), ct))
        {
            return pet;
        }

        return await ApplyRespectAsync(ctx, petId, ct);
    }

    /// <summary>
    /// Sends the scratcher to a tile next to the pet, and hands <paramref name="onArrival" /> to the
    /// end of that walk. False when there is nothing to walk: no avatar in the room, already within
    /// reach, or no neighbouring tile this avatar can get to — all three mean "do it now".
    /// </summary>
    /// <remarks>
    /// The pet is free to wander off while the walk is in flight, and then the scratch lands from
    /// wherever the walk ended. The reference emulator has the same race; catching it would mean
    /// pinning the pet for the duration, which is a rule of its own and needs its own evidence.
    /// </remarks>
    private async Task<bool> TryApproachPetAsync(
        ActionContext ctx,
        PetSnapshot pet,
        Func<CancellationToken, Task> onArrival,
        CancellationToken ct
    )
    {
        if (
            !_roomGrain._state.AvatarsByPlayerId.TryGetValue(
                ctx.PlayerId,
                out RoomObjectId objectId
            )
            || !_roomGrain._state.AvatarsByObjectId.TryGetValue(
                objectId.Value,
                out IRoomAvatar? avatar
            )
        )
        {
            return false;
        }

        // Any neighbouring tile will do, diagonals included -- standing next to the pet is the whole
        // requirement, and the hotel does not shuffle you onto a tidier one.
        if (Chebyshev(avatar.X, avatar.Y, pet.X, pet.Y) <= 1)
        {
            return false;
        }

        foreach ((int x, int y) in NeighboursNearest(avatar, pet))
        {
            if (!_roomGrain.MapModule.CanAvatarWalk(avatar, _roomGrain.MapModule.ToIdx(x, y)))
            {
                continue;
            }

            if (await _roomGrain.AvatarModule.WalkAvatarToThenAsync(avatar, x, y, onArrival, ct))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The pet's eight neighbours, the ones nearest the walker first.</summary>
    private static IEnumerable<(int X, int Y)> NeighboursNearest(
        IRoomAvatar avatar,
        PetSnapshot pet
    )
    {
        (int X, int Y)[] offsets =
        [
            (0, -1),
            (1, 0),
            (0, 1),
            (-1, 0),
            (1, -1),
            (1, 1),
            (-1, 1),
            (-1, -1),
        ];

        return offsets
            .Select(offset => (X: pet.X + offset.X, Y: pet.Y + offset.Y))
            .OrderBy(tile => Chebyshev(avatar.X, avatar.Y, tile.X, tile.Y));
    }

    private static int Chebyshev(int fromX, int fromY, int toX, int toY) =>
        Math.Max(Math.Abs(toX - fromX), Math.Abs(toY - fromY));

    private async Task<PetSnapshot?> ApplyRespectAsync(
        ActionContext ctx,
        int petId,
        CancellationToken ct
    )
    {
        // Re-read: a walk stands between the click and here, and the pet has been ticking all along.
        if (!_roomGrain._state.PetsById.TryGetValue(petId, out PetSnapshot? pet))
        {
            return null;
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

        // The scratcher's own daily allowance (respect.pet_daily_limit), and the only thing that
        // limits scratching. It is the number the client was handed at login and counts down from,
        // so a second rule of our own -- a per-pet cap, say -- would make that number a lie: the
        // button would still be offered while the server refused, or the other way round.
        if (!await _roomGrain._grainFactory.GetPlayerGrain(ctx.PlayerId).TryGivePetRespectAsync(ct))
        {
            return pet;
        }

        // The scratching arm. 999999999 is the client's CARRY_ITEM_EMPTY_HAND (AvatarLogic): no
        // object is drawn, the avatar plays the reach-out gesture for 1.5s and clears it itself, so
        // nothing here has to hold or expire a hand item. The live hotel sends this first, before
        // the experience.
        await BroadcastScratchGestureAsync(ctx.PlayerId);

        int respectXp = _roomGrain._roomConfig.Pet.RespectXpReward;
        PetSnapshot updated = await GrantXpAndLevelUpAsync(pet, respectXp, ct);
        updated = updated with
        {
            Respect = updated.Respect + 1,
            // Scratching is what a pet's happiness is for -- the live hotel pays 10 a scratch,
            // measured twice on the same pet.
            Happiness = Math.Clamp(
                updated.Happiness + Tuning.RespectHappinessReward,
                0,
                _roomGrain._roomConfig.Pet.HappinessCap
            ),
        };
        _roomGrain._state.PetsById[petId] = updated;

        if (_motionByPetId.TryGetValue(petId, out PetMotionState? motion))
        {
            motion.IsStatsDirty = true;
        }

        await BroadcastPetRespectNotificationAsync(updated);

        // The pet answers. The capture ends on these two and nothing else: its status gains
        // `gst joy` and it speaks a mood line ("*est euphorique*" on a French session). The text
        // itself is not in any source here -- it is content, and the operator's own pet_vocals rows
        // are where it comes from, so a type with no lines written for it just looks pleased.
        await SendPetUpdatedAsync(updated, ct, PetAnimation.JoyStatus);
        await BroadcastPetVocalAsync(updated, "GENERIC_HAPPY");

        // Detached, like the furniture events: the scratcher's progression must not sit between
        // the click and the animation. Both sides ride one event -- the hotel counts scratching
        // somebody's pet and having yours scratched as two different achievements.
        _roomGrain.PublishDetached(
            new PetRespectedEvent(ctx.PlayerId, updated.OwnerId, petId, updated.Type)
        );

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

    private Task BroadcastScratchGestureAsync(PlayerId playerId)
    {
        if (!_roomGrain._state.AvatarsByPlayerId.TryGetValue(playerId, out RoomObjectId objectId))
        {
            return Task.CompletedTask;
        }

        return _roomGrain.SendComposerToRoomAsync(
            new CarryObjectMessageComposer
            {
                UserId = objectId.Value,
                ItemType = PetAnimation.ScratchGestureItemType,
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
                Pet = pet,
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

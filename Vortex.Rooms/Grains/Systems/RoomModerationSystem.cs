using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Outgoing.Roomsettings;

namespace Vortex.Rooms.Grains.Systems;

/// <summary>
/// Kick/mute/ban enforcement. Kept on the room grain rather than a separate grain: kicking and
/// banning mutate live avatar state (<c>AvatarModule.RemoveAvatarFromPlayerAsync</c>) and muting is
/// read on every chat message via <c>RoomChatSystem</c>'s in-memory cache, both of which need the
/// synchronous, single-turn access only the owning grain has.
/// </summary>
public sealed class RoomModerationSystem(RoomGrain roomGrain)
{
    private readonly RoomGrain _roomGrain = roomGrain;

    public Task<bool> KickUserAsync(
        ActionContext actorCtx,
        PlayerId targetPlayerId,
        CancellationToken ct
    )
    {
        if (
            actorCtx.PlayerId <= 0
            || actorCtx.RoomId != _roomGrain._state.RoomId
            || actorCtx.PlayerId == targetPlayerId
        )
        {
            return Task.FromResult(false);
        }

        return KickUserGuardedAsync(actorCtx, targetPlayerId, ct);
    }

    private async Task<bool> KickUserGuardedAsync(
        ActionContext actorCtx,
        PlayerId targetPlayerId,
        CancellationToken ct
    )
    {
        if (
            !await CanModerateAsync(
                actorCtx,
                _roomGrain._state.RoomSnapshot.ModSettings.WhoCanKick,
                ModerationAction.Kick
            )
        )
        {
            return false;
        }

        return await KickUserInternalAsync(actorCtx, targetPlayerId, ct).ConfigureAwait(true);
    }

    /// <summary>
    /// Whether <paramref name="actorCtx"/> may perform a moderation action gated by
    /// <paramref name="setting"/> in this room. System/wired origins resolve to moderator and pass.
    /// </summary>
    /// <summary>
    /// Flips the room-info panel's "mute all" switch and tells the room where it landed. Gated by
    /// the room's own who-can-mute setting: the switch silences everyone at once, so it is the same
    /// authority as muting one person, not a separate one.
    /// </summary>
    /// <returns>False when the actor was not allowed to touch it, in which case nothing changed.</returns>
    public Task<bool> ToggleAllInRoomMuteAsync(ActionContext actorCtx)
    {
        if (actorCtx.PlayerId <= 0 || actorCtx.RoomId != _roomGrain._state.RoomId)
        {
            return Task.FromResult(false);
        }

        return ToggleAllInRoomMuteGuardedAsync(actorCtx);
    }

    public RoomMuteStateSnapshot GetMuteState(PlayerId viewerId) =>
        new()
        {
            AllInRoomMuted = _roomGrain._state.AllInRoomMuted,
            // Rights, not the full controller level: this is drawn on a room card the navigator
            // asks for constantly, and it is the same test the reference emulator ships.
            CanMute = _roomGrain.SecurityModule.HasExplicitRights(viewerId),
        };

    private async Task<bool> ToggleAllInRoomMuteGuardedAsync(ActionContext actorCtx)
    {
        if (
            !await CanModerateAsync(
                actorCtx,
                _roomGrain._state.RoomSnapshot.ModSettings.WhoCanMute,
                ModerationAction.Mute
            )
        )
        {
            return false;
        }

        _roomGrain._state.AllInRoomMuted = !_roomGrain._state.AllInRoomMuted;

        // To the whole room, not just whoever pressed it: the flag is what redraws the button, and
        // every rights-holder standing here has the same button.
        await _roomGrain
            .SendComposerToRoomAsync(
                new MuteAllInRoomEventMessageComposer
                {
                    AllMuted = _roomGrain._state.AllInRoomMuted,
                }
            )
            .ConfigureAwait(true);

        return true;
    }

    private async Task<bool> CanModerateAsync(
        ActionContext actorCtx,
        ModSettingType setting,
        ModerationAction action
    )
    {
        RoomControllerType level = await _roomGrain
            .SecurityModule.GetControllerLevelAsync(actorCtx)
            .ConfigureAwait(true);

        bool hasStaffCapability = await _roomGrain
            .SecurityModule.HasStaffModerationCapabilityAsync(actorCtx, action)
            .ConfigureAwait(true);

        if (RoomModerationPolicy.CanModerate(level, setting, hasStaffCapability))
        {
            return true;
        }

        _roomGrain._logger.LogDebug(
            "Player {ActorId} lacks {Setting} moderation rights in room {RoomId} (level {Level}).",
            actorCtx.PlayerId,
            setting,
            _roomGrain._state.RoomId,
            level
        );

        return false;
    }

    private async Task<bool> KickUserInternalAsync(
        ActionContext ctx,
        PlayerId targetPlayerId,
        CancellationToken ct
    )
    {
        try
        {
            if (
                targetPlayerId <= 0
                || !_roomGrain._state.AvatarsByPlayerId.ContainsKey(targetPlayerId)
            )
            {
                return false;
            }

            await _roomGrain
                .AvatarModule.RemoveAvatarFromPlayerAsync(ctx, targetPlayerId, ct)
                .ConfigureAwait(true);

            await _roomGrain
                ._events.PublishAsync(
                    new PlayerKickedFromRoomEvent(
                        ctx.PlayerId,
                        targetPlayerId,
                        _roomGrain._state.RoomId.Value
                    ),
                    ct
                )
                .ConfigureAwait(true);

            return true;
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Failed to kick player {TargetPlayerId} from room {RoomId}.",
                targetPlayerId,
                _roomGrain._state.RoomId
            );

            return false;
        }
    }

    public async Task<bool> MuteUserAsync(
        ActionContext actorCtx,
        PlayerId targetPlayerId,
        int durationSeconds,
        CancellationToken ct
    )
    {
        if (
            actorCtx.PlayerId <= 0
            || targetPlayerId <= 0
            || actorCtx.RoomId != _roomGrain._state.RoomId
        )
        {
            return false;
        }

        if (durationSeconds <= 0 || actorCtx.PlayerId == targetPlayerId)
        {
            return false;
        }

        if (
            !await CanModerateAsync(
                actorCtx,
                _roomGrain._state.RoomSnapshot.ModSettings.WhoCanMute,
                ModerationAction.Mute
            )
        )
        {
            return false;
        }

        return await MuteUserInternalAsync(actorCtx.PlayerId, targetPlayerId, durationSeconds, ct)
            .ConfigureAwait(true);
    }

    private async Task<bool> MuteUserInternalAsync(
        PlayerId actorPlayerId,
        PlayerId targetPlayerId,
        int durationSeconds,
        CancellationToken ct
    )
    {
        DateTime expiresUtc = DateTime.UtcNow.AddSeconds(durationSeconds);

        try
        {
            await _roomGrain._moderationStore.MuteAsync(
                _roomGrain._state.RoomId.Value,
                targetPlayerId,
                expiresUtc,
                ct
            );
            _roomGrain._state.MuteExpiresUtc[targetPlayerId] = expiresUtc;

            await _roomGrain
                ._events.PublishAsync(
                    new PlayerMutedInRoomEvent(
                        actorPlayerId,
                        targetPlayerId,
                        _roomGrain._state.RoomId.Value,
                        durationSeconds
                    ),
                    ct
                )
                .ConfigureAwait(true);

            return true;
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Failed to mute player {TargetPlayerId} in room {RoomId}.",
                targetPlayerId,
                _roomGrain._state.RoomId
            );
        }

        return false;
    }

    public async Task<bool> BanUserAsync(
        ActionContext actorCtx,
        PlayerId targetPlayerId,
        int durationSeconds,
        CancellationToken ct
    )
    {
        try
        {
            if (
                actorCtx.PlayerId <= 0
                || targetPlayerId <= 0
                || actorCtx.RoomId != _roomGrain._state.RoomId
            )
            {
                return false;
            }

            if (durationSeconds <= 0 || actorCtx.PlayerId == targetPlayerId)
            {
                return false;
            }

            if (
                !await CanModerateAsync(
                    actorCtx,
                    _roomGrain._state.RoomSnapshot.ModSettings.WhoCanBan,
                    ModerationAction.Ban
                )
            )
            {
                return false;
            }

            DateTime expiresUtc = DateTime.UtcNow.AddSeconds(durationSeconds);

            await _roomGrain._moderationStore.BanAsync(
                _roomGrain._state.RoomId.Value,
                targetPlayerId,
                expiresUtc,
                ct
            );
            await _roomGrain
                .AvatarModule.RemoveAvatarFromPlayerAsync(actorCtx, targetPlayerId, ct)
                .ConfigureAwait(true);

            await _roomGrain
                ._events.PublishAsync(
                    new PlayerBannedInRoomEvent(
                        actorCtx.PlayerId,
                        targetPlayerId,
                        _roomGrain._state.RoomId.Value,
                        durationSeconds
                    ),
                    ct
                )
                .ConfigureAwait(true);

            return true;
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Failed to ban player {TargetPlayerId} in room {RoomId}.",
                targetPlayerId,
                _roomGrain._state.RoomId
            );

            return false;
        }
    }

    /// <summary>
    /// Refreshes this room's cached copy of a hotel-wide staff mute. Deliberately unauthorized and
    /// unaudited: the sanction itself was authorized, persisted and audited by the caller, and this
    /// is only the room catching up with it. A player who is not here is ignored — they will pick
    /// the mute up from their entry snapshot wherever they go next.
    /// </summary>
    public Task SetHotelMuteAsync(PlayerId targetPlayerId, DateTime? expiresUtc)
    {
        if (!_roomGrain._state.AvatarsByPlayerId.ContainsKey(targetPlayerId))
        {
            return Task.CompletedTask;
        }

        if (expiresUtc is DateTime until && until > DateTime.UtcNow)
        {
            _roomGrain._state.HotelMuteExpiresUtc[targetPlayerId] = until;
        }
        else
        {
            _roomGrain._state.HotelMuteExpiresUtc.Remove(targetPlayerId);
        }

        return Task.CompletedTask;
    }

    public async Task<bool> UnmuteUserAsync(
        ActionContext actorCtx,
        PlayerId targetPlayerId,
        CancellationToken ct
    )
    {
        try
        {
            if (
                actorCtx.PlayerId <= 0
                || targetPlayerId <= 0
                || actorCtx.RoomId != _roomGrain._state.RoomId
            )
            {
                return false;
            }

            if (
                !await CanModerateAsync(
                    actorCtx,
                    _roomGrain._state.RoomSnapshot.ModSettings.WhoCanMute,
                    ModerationAction.Mute
                )
            )
            {
                return false;
            }

            await _roomGrain
                ._moderationStore.UnmuteAsync(_roomGrain._state.RoomId.Value, targetPlayerId, ct)
                .ConfigureAwait(true);
            _roomGrain._state.MuteExpiresUtc.Remove(targetPlayerId);

            return true;
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Failed to unmute player {TargetPlayerId} in room {RoomId}.",
                targetPlayerId,
                _roomGrain._state.RoomId
            );

            return false;
        }
    }

    public async Task<bool> UnbanUserAsync(
        ActionContext actorCtx,
        PlayerId targetPlayerId,
        CancellationToken ct
    )
    {
        try
        {
            if (
                actorCtx.PlayerId <= 0
                || targetPlayerId <= 0
                || actorCtx.RoomId != _roomGrain._state.RoomId
            )
            {
                return false;
            }

            if (
                !await CanModerateAsync(
                    actorCtx,
                    _roomGrain._state.RoomSnapshot.ModSettings.WhoCanBan,
                    ModerationAction.Ban
                )
            )
            {
                return false;
            }

            await _roomGrain
                ._moderationStore.UnbanAsync(_roomGrain._state.RoomId.Value, targetPlayerId, ct)
                .ConfigureAwait(true);

            return true;
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Failed to unban player {TargetPlayerId} in room {RoomId}.",
                targetPlayerId,
                _roomGrain._state.RoomId
            );

            return false;
        }
    }

    /// <summary>
    /// Applies the room-tool checkboxes on behalf of a staff member who is not in the room and is
    /// not its owner. Authorization has already happened at the handler; this deliberately does not
    /// consult <see cref="CanModerateAsync"/>, whose whole job is to answer "may this <i>occupant</i>
    /// do this here", which is the wrong question for a hotel moderator.
    /// </summary>
    public async Task<bool> ApplyStaffRoomActionsAsync(
        PlayerId actorPlayerId,
        bool unlockDoor,
        bool resetNameAndDescription,
        bool kickUsers,
        CancellationToken ct
    )
    {
        if (actorPlayerId <= 0 || (!unlockDoor && !resetNameAndDescription && !kickUsers))
        {
            return false;
        }

        bool applied = false;

        try
        {
            if (unlockDoor || resetNameAndDescription)
            {
                applied = await PersistStaffRoomActionsAsync(
                        unlockDoor,
                        resetNameAndDescription,
                        ct
                    )
                    .ConfigureAwait(true);
            }

            if (kickUsers)
            {
                // Snapshot the ids first: KickUserInternalAsync mutates AvatarsByPlayerId, so
                // iterating it live would throw partway through and leave the room half-emptied.
                PlayerId[] occupants = [.. _roomGrain._state.AvatarsByPlayerId.Keys];

                foreach (PlayerId occupant in occupants)
                {
                    applied |= await KickUserInternalAsync(
                            ActionContext.CreateForWired(_roomGrain._state.RoomId),
                            occupant,
                            ct
                        )
                        .ConfigureAwait(true);
                }
            }

            await _roomGrain
                ._events.PublishAsync(
                    new RoomModeratedByStaffEvent(
                        actorPlayerId,
                        _roomGrain._state.RoomId.Value,
                        unlockDoor,
                        resetNameAndDescription,
                        kickUsers
                    ),
                    ct
                )
                .ConfigureAwait(true);

            return applied;
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Failed to apply staff room actions from {ActorPlayerId} to room {RoomId}.",
                actorPlayerId,
                _roomGrain._state.RoomId
            );

            return false;
        }
    }

    private async Task<bool> PersistStaffRoomActionsAsync(
        bool unlockDoor,
        bool resetNameAndDescription,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _roomGrain
            ._dbCtxFactory.CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        RoomEntity? entity = await dbCtx
            .Rooms.FirstOrDefaultAsync(r => r.Id == _roomGrain._state.RoomId.Value, ct)
            .ConfigureAwait(true);

        if (entity is null)
        {
            return false;
        }

        if (unlockDoor)
        {
            entity.DoorMode = RoomDoorModeType.Open;
            entity.Password = null;
        }

        if (resetNameAndDescription)
        {
            entity.Name = _roomGrain._roomConfig.ModeratedRoomNamePlaceholder;
            entity.Description = string.Empty;
        }

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        // The live snapshot has to follow, or the navigator keeps advertising the old name until the
        // grain is next deactivated.
        _roomGrain._state.RoomSnapshot = _roomGrain._state.RoomSnapshot with
        {
            Name = entity.Name,
            Description = entity.Description ?? string.Empty,
            DoorMode = entity.DoorMode,
            Password = entity.Password ?? string.Empty,
        };

        return true;
    }
}

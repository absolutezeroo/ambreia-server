using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Roomsettings;

namespace Vortex.Rooms.Grains.Modules;

/// <summary>
/// Granting and taking away the room's explicit rights.
/// </summary>
/// <remarks>
/// <para>
/// This sits with the controllers and the ban list rather than in the settings partial because it is
/// the same subject: every one of these writes ends in a reload of the controller list and a refresh
/// of somebody's controller level, both of which already live here.
/// </para>
/// <para>
/// Unchecked, in the same sense as <c>LoadControllersAsync</c>: the grain decides who may call, and
/// each of these runs after that decision. The authorisation stays in the partial, next to the
/// public grain interface it guards, so a reader looking for "who may take my rights away" finds it
/// where the method they can call is declared.
/// </para>
/// </remarks>
public sealed partial class RoomSecurityModule
{
    /// <summary>Adds <paramref name="target"/> to the room's rights, if they are not already on it.</summary>
    public async Task GrantRightsAsync(PlayerId actor, PlayerId target, CancellationToken ct)
    {
        try
        {
            await using VortexDbContext dbCtx = await _roomGrain
                ._dbCtxFactory.CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            bool alreadyHasRights = await dbCtx
                .RoomRights.AnyAsync(
                    r =>
                        r.RoomEntityId == _roomGrain._state.RoomId.Value
                        && r.PlayerEntityId == target.Value
                        && r.DeletedAt == null,
                    ct
                )
                .ConfigureAwait(true);

            if (alreadyHasRights)
            {
                return;
            }

            dbCtx.RoomRights.Add(
                new RoomRightEntity
                {
                    RoomEntityId = _roomGrain._state.RoomId.Value,
                    PlayerEntityId = target.Value,
                }
            );

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

            _roomGrain._state.PlayerIdsWithRights.Add(target);

            await PushControllersToAsync(actor, ct).ConfigureAwait(true);
            await RefreshControllerLevelForPlayerAsync(target, ct).ConfigureAwait(true);

            await _roomGrain
                ._events.PublishAsync(
                    new RoomRightsChangedEvent(
                        actor,
                        _roomGrain._state.RoomId.Value,
                        target,
                        "granted"
                    ),
                    ct
                )
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogError(
                ex,
                "Failed to assign rights to player {Target} in room {RoomId}.",
                target,
                _roomGrain._state.RoomId
            );
        }
    }

    /// <summary>Takes the rights away from each of <paramref name="targets"/>.</summary>
    public async Task RevokeRightsAsync(
        PlayerId actor,
        ImmutableArray<PlayerId> targets,
        CancellationToken ct
    )
    {
        try
        {
            await using VortexDbContext dbCtx = await _roomGrain
                ._dbCtxFactory.CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            int[] targetValues = [.. targets.Select(t => t.Value)];

            List<RoomRightEntity> rows = await dbCtx
                .RoomRights.Where(r =>
                    r.RoomEntityId == _roomGrain._state.RoomId.Value
                    && targetValues.AsEnumerable().Contains(r.PlayerEntityId)
                    && r.DeletedAt == null
                )
                .ToListAsync(ct)
                .ConfigureAwait(true);

            foreach (RoomRightEntity row in rows)
            {
                dbCtx.RoomRights.Remove(row);
            }

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

            foreach (PlayerId target in targets)
            {
                _roomGrain._state.PlayerIdsWithRights.Remove(target);
            }

            await PushControllersToAsync(actor, ct).ConfigureAwait(true);

            await Task.WhenAll(
                    targets.Select(target => RefreshControllerLevelForPlayerAsync(target, ct))
                )
                .ConfigureAwait(true);

            // One record per target rather than one for the batch: an investigation searches by the
            // account that lost the rights at least as often as by the owner who took them away.
            foreach (PlayerId target in targets)
            {
                await _roomGrain
                    ._events.PublishAsync(
                        new RoomRightsChangedEvent(
                            actor,
                            _roomGrain._state.RoomId.Value,
                            target,
                            "removed"
                        ),
                        ct
                    )
                    .ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogError(
                ex,
                "Failed to remove rights in room {RoomId}.",
                _roomGrain._state.RoomId
            );
        }
    }

    /// <summary>Clears the room's rights list.</summary>
    public async Task RevokeAllRightsAsync(PlayerId actor, CancellationToken ct)
    {
        try
        {
            await using VortexDbContext dbCtx = await _roomGrain
                ._dbCtxFactory.CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            List<RoomRightEntity> rows = await dbCtx
                .RoomRights.Where(r =>
                    r.RoomEntityId == _roomGrain._state.RoomId.Value && r.DeletedAt == null
                )
                .ToListAsync(ct)
                .ConfigureAwait(true);

            List<PlayerId> targets = [.. rows.Select(r => (PlayerId)r.PlayerEntityId)];

            foreach (RoomRightEntity row in rows)
            {
                dbCtx.RoomRights.Remove(row);
            }

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

            foreach (PlayerId target in targets)
            {
                _roomGrain._state.PlayerIdsWithRights.Remove(target);
            }

            // Empty rather than a reload: the list was just cleared, and asking the database to
            // confirm that would be a round trip to learn what this method already knows.
            await _roomGrain
                ._grainFactory.GetPlayerPresenceGrain(actor)
                .SendComposerAsync(
                    new FlatControllersEventMessageComposer
                    {
                        RoomId = _roomGrain._state.RoomId,
                        Controllers = [],
                    }
                )
                .ConfigureAwait(true);

            await Task.WhenAll(
                    targets.Select(target => RefreshControllerLevelForPlayerAsync(target, ct))
                )
                .ConfigureAwait(true);

            // No target: "cleared the room's rights" is one act, and the individual losses are not
            // separate decisions the way a targeted removal is.
            await _roomGrain
                ._events.PublishAsync(
                    new RoomRightsChangedEvent(
                        actor,
                        _roomGrain._state.RoomId.Value,
                        null,
                        "removed_all"
                    ),
                    ct
                )
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogError(
                ex,
                "Failed to remove all rights in room {RoomId}.",
                _roomGrain._state.RoomId
            );
        }
    }

    /// <summary>A rights-holder giving up their own rights. Nobody else's decision, so no owner check.</summary>
    public async Task RevokeOwnRightsAsync(PlayerId actor, CancellationToken ct)
    {
        try
        {
            await using VortexDbContext dbCtx = await _roomGrain
                ._dbCtxFactory.CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            RoomRightEntity? row = await dbCtx
                .RoomRights.FirstOrDefaultAsync(
                    r =>
                        r.RoomEntityId == _roomGrain._state.RoomId.Value
                        && r.PlayerEntityId == actor.Value
                        && r.DeletedAt == null,
                    ct
                )
                .ConfigureAwait(true);

            if (row is null)
            {
                return;
            }

            dbCtx.RoomRights.Remove(row);
            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

            _roomGrain._state.PlayerIdsWithRights.Remove(actor);

            await PushControllersToAsync(actor, ct).ConfigureAwait(true);
            await RefreshControllerLevelForPlayerAsync(actor, ct).ConfigureAwait(true);

            await _roomGrain
                ._events.PublishAsync(
                    new RoomRightsChangedEvent(
                        actor,
                        _roomGrain._state.RoomId.Value,
                        actor,
                        "gave_up"
                    ),
                    ct
                )
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogError(
                ex,
                "Failed to remove own rights for player {Actor} in room {RoomId}.",
                actor,
                _roomGrain._state.RoomId
            );
        }
    }

    /// <summary>
    /// Re-reads the rights list and sends it to one player — the owner who just changed it, whose
    /// dialog is showing the list as it was a moment ago.
    /// </summary>
    private async Task PushControllersToAsync(PlayerId actor, CancellationToken ct)
    {
        ImmutableArray<RoomControllerSnapshot> controllers = await LoadControllersAsync(ct)
            .ConfigureAwait(true);

        await _roomGrain
            ._grainFactory.GetPlayerPresenceGrain(actor)
            .SendComposerAsync(
                new FlatControllersEventMessageComposer
                {
                    RoomId = _roomGrain._state.RoomId,
                    Controllers = controllers,
                }
            )
            .ConfigureAwait(true);
    }
}

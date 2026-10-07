using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Primitives.Action;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;

namespace Vortex.Rooms.Grains.Modules;

public sealed partial class RoomSecurityModule(RoomGrain roomGrain)
{
    private readonly RoomGrain _roomGrain = roomGrain;

    public async Task<bool> CanManipulateFurniAsync(ActionContext ctx)
    {
        // Rights is the floor for building anywhere.
        RoomControllerType controllerLevel = await GetControllerLevelAsync(ctx);

        return controllerLevel >= RoomControllerType.Rights;
    }

    public async Task<bool> CanUseFurniAsync(ActionContext ctx, FurnitureUsageType usageType)
    {
        RoomControllerType controllerLevel = await GetControllerLevelAsync(ctx);

        if (usageType == FurnitureUsageType.Nobody)
        {
            return false;
        }

        if (usageType == FurnitureUsageType.Controller)
        {
            if (controllerLevel < RoomControllerType.Rights)
            {
                return false;
            }
        }

        return true;
    }

    public async Task<bool> CanPlaceFurniAsync(ActionContext ctx)
    {
        return await CanManipulateFurniAsync(ctx);
    }

    public async Task<FurniturePickupType> GetFurniPickupTypeAsync(ActionContext ctx)
    {
        if (ctx.Origin == ActionOrigin.System)
        {
            return FurniturePickupType.SendToOwner;
        }

        if (await GetControllerLevelAsync(ctx) >= RoomControllerType.GroupAdmin)
        {
            return FurniturePickupType.SendToOwner;
        }

        return FurniturePickupType.None;
    }

    public async Task<bool> GetIsRoomOwnerAsync(ActionContext ctx)
    {
        bool isExplicitOwner = _roomGrain._state.RoomSnapshot.OwnerId == ctx.PlayerId;

        PermissionSet permissions = await ResolvePermissionsAsync(ctx);

        return RoomSecurityPolicy.IsRoomOwner(permissions, isExplicitOwner);
    }

    /// <summary>
    /// Owner-or-rights, answered from live state alone: no permission resolution and no grain call.
    /// For a hot path where <see cref="GetControllerLevelAsync"/>'s permission lookup would be paid
    /// per call; nothing calls it yet.
    /// </summary>
    public bool HasExplicitRights(PlayerId playerId) =>
        _roomGrain._state.RoomSnapshot.OwnerId == playerId
        || _roomGrain._state.PlayerIdsWithRights.Contains(playerId);

    public async Task<RoomControllerType> GetControllerLevelAsync(ActionContext ctx)
    {
        if (ctx.Origin == ActionOrigin.System)
        {
            return RoomControllerType.Moderator;
        }

        PermissionSet permissions = await ResolvePermissionsAsync(ctx);

        bool isExplicitOwner = _roomGrain._state.RoomSnapshot.OwnerId == ctx.PlayerId;
        bool hasExplicitRights = _roomGrain._state.PlayerIdsWithRights.Contains(ctx.PlayerId);

        return RoomSecurityPolicy.ResolveControllerLevel(
            ctx.Origin,
            permissions,
            isExplicitOwner,
            hasExplicitRights,
            RoomGroupContext.None
        );
    }

    private Task<PermissionSet> ResolvePermissionsAsync(ActionContext ctx)
    {
        if (ctx.PlayerId == PlayerId.Invalid)
        {
            return Task.FromResult(PermissionSet.Empty);
        }

        return _roomGrain._permissionService.ResolveForPlayerAsync(ctx.PlayerId);
    }

    public async Task RefreshControllerLevelForPlayerAsync(PlayerId playerId, CancellationToken ct)
    {
        RoomControllerType controllerLevel = await GetControllerLevelAsync(
            new ActionContext { PlayerId = playerId, Origin = ActionOrigin.Player }
        );

        await _roomGrain
            ._grainFactory.GetPlayerPresenceGrain(playerId)
            .OnControllerLevelUpdatedAsync(_roomGrain.RoomId, controllerLevel, ct)
            .ConfigureAwait(true);

        // The presence notification only re-draws the subject's own UI. Everyone else reads the
        // level off the avatar's `flatctrl` status, which is stamped once at spawn -- without this
        // the rights star only appears after the player leaves and re-enters. `AddStatus` marks the
        // object dirty, so the room tick carries it out with the next status batch.
        if (
            !_roomGrain._state.AvatarsByPlayerId.TryGetValue(playerId, out RoomObjectId objectId)
            || !_roomGrain._state.AvatarsByObjectId.TryGetValue(objectId, out IRoomAvatar? avatar)
        )
        {
            return;
        }

        avatar.AddStatus(
            AvatarStatusType.FlatControl,
            ((int)controllerLevel).ToString(CultureInfo.InvariantCulture)
        );
    }

    /// <summary>
    /// The room's explicitly assigned rights, straight from the table. Unchecked: the grain's own
    /// broadcasts call this after a rights change it has already authorised, and the packet-facing
    /// path goes through <c>RoomGrain.GetControllersAsync</c>, which gates on ownership first.
    /// </summary>
    public async Task<ImmutableArray<RoomControllerSnapshot>> LoadControllersAsync(
        CancellationToken ct
    )
    {
        try
        {
            await using VortexDbContext dbCtx = await _roomGrain
                ._dbCtxFactory.CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            List<RoomControllerSnapshot> result = await dbCtx
                .RoomRights.AsNoTracking()
                .Include(r => r.PlayerEntity)
                .Where(r => r.RoomEntityId == _roomGrain.RowId.Value && r.DeletedAt == null)
                .Select(r => new RoomControllerSnapshot
                {
                    PlayerId = r.PlayerEntityId,
                    Name = r.PlayerEntity != null ? r.PlayerEntity.Name : string.Empty,
                })
                .ToListAsync(ct)
                .ConfigureAwait(true);

            return [.. result];
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogError(
                ex,
                "Failed to load controllers for room {RoomId}.",
                _roomGrain._state.RoomId
            );

            return [];
        }
    }
}

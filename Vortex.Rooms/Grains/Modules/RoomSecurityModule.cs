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
using Vortex.Primitives.Groups.Enums;
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
        // Rights is the floor for building anywhere. In a guild room the group standing is already
        // folded into the resolved level (GroupRights/GroupAdmin), so guild members and classic
        // rights-holders are both covered without a separate guild branch here.
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

    /// <summary>
    /// Returns the rentable-space floor item if <paramref name="ctx"/> player owns
    /// <paramref name="itemId"/> and the item sits inside their active rented zone.
    /// Used to gate move/use operations for renters.
    /// </summary>
    public async Task<IRoomFloorItem?> FindRentedSpaceForOwnedItemAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct
    )
    {
        if (!_roomGrain._state.ItemsById.TryGetValue(itemId, out IRoomItem? item))
        {
            return null;
        }

        if (item.OwnerId.Value != ctx.PlayerId.Value)
        {
            return null;
        }

        IRoomFloorItem? spaceItem = await FindRentedSpaceForPlayerAsync(ctx.PlayerId.Value, ct);

        if (spaceItem is null)
        {
            return null;
        }

        if (!_roomGrain.FurniModule.GetTileIdForFloorItem(spaceItem, out List<int> spaceTileIds))
        {
            return null;
        }

        int itemTile = _roomGrain.MapModule.ToIdx(item.X, item.Y);

        return spaceTileIds.Contains(itemTile) ? spaceItem : null;
    }

    /// <summary>
    /// Returns the rentable-space floor item that <paramref name="playerId"/> is currently
    /// renting in this room, or null if none exists.
    /// </summary>
    public async Task<IRoomFloorItem?> FindRentedSpaceForPlayerAsync(
        int playerId,
        CancellationToken ct
    )
    {
        if (playerId <= 0)
        {
            return null;
        }

        DateTime now = DateTime.UtcNow;
        int roomId = _roomGrain._state.RoomId.Value;

        await using VortexDbContext db = await _roomGrain._dbCtxFactory.CreateDbContextAsync(ct);

        int furnitureId = await (
            from rrs in db.RoomRentableSpaces.AsNoTracking()
            join f in db.Furnitures.AsNoTracking() on rrs.FurnitureEntityId equals f.Id
            where
                rrs.RenterPlayerEntityId == playerId
                && rrs.DeletedAt == null
                && rrs.RentedUntil > now
                && f.RoomEntityId == roomId
                && f.DeletedAt == null
            select rrs.FurnitureEntityId
        ).FirstOrDefaultAsync(ct);

        if (furnitureId == 0)
        {
            return null;
        }

        RoomObjectId objectId = new(furnitureId);

        return _roomGrain._state.ItemsById.TryGetValue(objectId, out IRoomItem? item)
            ? item as IRoomFloorItem
            : null;
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

    /// <summary>
    /// Whether the subject holds the staff capability for <paramref name="action"/> (or the
    /// room-wide moderate-any / wildcard). Kept separate from the controller level because staff
    /// moderation must not imply build rights — see <see cref="RoomModerationPolicy.CanModerate"/>.
    /// </summary>
    public async Task<bool> HasStaffModerationCapabilityAsync(
        ActionContext ctx,
        ModerationAction action
    )
    {
        // An unfilled context is nobody and moderates nothing. Everything else that is not a player
        // -- system, wired, a bot, a plugin -- is the server acting on its own behalf and carries no
        // capabilities to resolve.
        if (ctx.Origin == ActionOrigin.None)
        {
            return false;
        }

        if (ctx.Origin != ActionOrigin.Player)
        {
            return true;
        }

        PermissionSet permissions = await ResolvePermissionsAsync(ctx);

        return ModerationPolicy.IsAllowed(permissions, action);
    }

    public async Task<bool> GetIsRoomOwnerAsync(ActionContext ctx)
    {
        bool isExplicitOwner = _roomGrain._state.RoomSnapshot.OwnerId == ctx.PlayerId;

        PermissionSet permissions = await ResolvePermissionsAsync(ctx);

        return RoomSecurityPolicy.IsRoomOwner(permissions, isExplicitOwner);
    }

    /// <summary>
    /// Owner-or-rights, answered from live state alone: no permission resolution and no grain call.
    /// For the paths that run on every single chat line, where
    /// <see cref="GetControllerLevelAsync"/>'s permission lookup would be paid per message.
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
            ResolveGroupContext(ctx.PlayerId)
        );
    }

    /// <summary>
    /// The subject's standing in the guild that owns this room, read from the live membership cache
    /// hydrated on room load. Returns <see cref="RoomGroupContext.None"/> for non-guild rooms and for
    /// players who hold no membership row.
    /// </summary>
    private RoomGroupContext ResolveGroupContext(PlayerId playerId)
    {
        if (_roomGrain._state.RoomSnapshot.GroupId is null)
        {
            return RoomGroupContext.None;
        }

        if (!_roomGrain._state.GroupMemberRanks.TryGetValue(playerId, out GroupMemberRank rank))
        {
            return RoomGroupContext.None;
        }

        return new RoomGroupContext(
            IsMember: true,
            IsAdmin: rank == GroupMemberRank.Admin,
            MembersCanDecorate: !_roomGrain._state.GroupAdminOnlyDecoration
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

    /// <summary>
    /// Whether a hotel-wide capability holds for this player, for the grain methods that answer to a
    /// staff power rather than to an in-room controller level.
    /// </summary>
    /// <remarks>
    /// Those methods used to leave the check to their packet handler. A handler is not a security
    /// boundary: the method is a member of a public grain interface, callable by anything in the
    /// cluster that can name the room (ROOMG-GATE-038). The handler keeps its own check — it answers
    /// the client without waking a room — and this is the one that decides.
    /// </remarks>
    public async Task<bool> HasCapabilityAsync(PlayerId actor, string capability)
    {
        if (actor <= 0)
        {
            return false;
        }

        PermissionSet permissions = await _roomGrain
            ._permissionService.ResolveForPlayerAsync(actor)
            .ConfigureAwait(true);

        return permissions.Has(capability);
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
                .Where(r => r.RoomEntityId == _roomGrain._state.RoomId.Value && r.DeletedAt == null)
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

    /// <summary>
    /// The room's bans that have not yet expired. Unchecked, same contract as
    /// <see cref="LoadControllersAsync"/>.
    /// </summary>
    public async Task<ImmutableArray<RoomControllerSnapshot>> LoadBannedUsersAsync(
        CancellationToken ct
    )
    {
        try
        {
            await using VortexDbContext dbCtx = await _roomGrain
                ._dbCtxFactory.CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            DateTime now = DateTime.UtcNow;

            List<RoomControllerSnapshot> result = await dbCtx
                .RoomBans.AsNoTracking()
                .Include(b => b.PlayerEntity)
                .Where(b =>
                    b.RoomEntityId == _roomGrain._state.RoomId.Value
                    && b.DeletedAt == null
                    && b.DateExpires > now
                )
                .Select(b => new RoomControllerSnapshot
                {
                    PlayerId = b.PlayerEntityId,
                    Name = b.PlayerEntity.Name,
                })
                .ToListAsync(ct)
                .ConfigureAwait(true);

            return [.. result];
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogError(
                ex,
                "Failed to load banned users for room {RoomId}.",
                _roomGrain._state.RoomId
            );

            return [];
        }
    }
}

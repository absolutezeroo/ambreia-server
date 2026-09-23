using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Primitives.Action;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Snapshots.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Wired;
using Vortex.Primitives.Rooms.Snapshots.Wired.Variables;
using Vortex.Primitives.Rooms.Wired.Variable;
using Vortex.Protocol.Messages.Incoming.Userdefinedroomevents;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Addons;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Variables;
using Vortex.Rooms.Wired;
using Vortex.Rooms.Wired.Variables;

namespace Vortex.Rooms.Grains;

public sealed partial class RoomGrain
{
    public async Task<bool> PlaceFloorItemAsync(
        ActionContext ctx,
        FurnitureItemSnapshot item,
        int x,
        int y,
        Rotation rot,
        CancellationToken ct
    )
    {
        try
        {
            if (!await ActionModule.PlaceFloorItemAsync(ctx, item, x, y, rot, ct))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to place floor item {ItemId} at ({X},{Y}) in room {RoomId}",
                item.ItemId,
                x,
                y,
                _state.RoomId
            );

            return false;
        }
    }

    public async Task<bool> MoveFloorItemByIdAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        int x,
        int y,
        Rotation rot,
        CancellationToken ct
    )
    {
        try
        {
            if (!await ActionModule.MoveFloorItemByIdAsync(ctx, itemId, x, y, rot, ct))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to move floor item {ItemId} to ({X},{Y}) in room {RoomId}",
                itemId,
                x,
                y,
                _state.RoomId
            );

            return false;
        }
    }

    public async Task<bool> ApplyWiredUpdateAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        WiredUpdateRequest update,
        CancellationToken ct
    )
    {
        try
        {
            if (!await ActionModule.ApplyWiredUpdateAsync(ctx, itemId, update, ct))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to apply wired update to item {ItemId} in room {RoomId}",
                itemId,
                _state.RoomId
            );

            return false;
        }
    }

    public Task<RoomFloorItemSnapshot?> GetFloorItemSnapshotByIdAsync(
        RoomObjectId itemId,
        CancellationToken ct
    ) =>
        Task.FromResult(
            _state.ItemsById.TryGetValue(itemId, out IRoomItem? item)
            && item is IRoomFloorItem floorItem
                ? floorItem.GetSnapshot()
                : null
        );

    public Task<ImmutableArray<RoomFloorItemSnapshot>> GetAllFloorItemSnapshotsAsync(
        CancellationToken ct
    ) => FurniModule.GetAllFloorItemSnapshotsAsync(ct);

    public async Task<WiredDataSnapshot?> GetWiredDataSnapshotByFloorItemIdAsync(
        RoomObjectId itemId,
        PlayerId playerId,
        CancellationToken ct
    )
    {
        if (
            !_state.ItemsById.TryGetValue(itemId, out IRoomItem? item)
            || item.Logic is not FurnitureWiredLogic wiredLogic
        )
        {
            return null;
        }

        // Make sure the box's persisted config is deserialized before we snapshot it — opening the
        // wired editor can happen before the wired tick has ever processed this furni's stack, and
        // BuildSnapshot dereferences the (otherwise null) wired data.
        await wiredLogic.LoadWiredAsync(ct);

        WiredDataSnapshot snapshot = wiredLogic.GetSnapshot();

        if (wiredLogic is WiredAddonGlobalPlaceholder)
        {
            // The same reason as the reference variable below, for the other rooms' placeholders;
            // this room's own are left out, since the dialog offers them "from another room".
            await using VortexDbContext placeholderDbCtx = await _dbCtxFactory.CreateDbContextAsync(
                ct
            );

            return snapshot with
            {
                ContextSnapshots =
                [
                    .. snapshot.ContextSnapshots,
                    new WiredSharedPlaceholderListSnapshot
                    {
                        ContextType = WiredContextType.SharedGlobalPlaceholders,
                        Elements = await WiredGlobalPlaceholderIndex.LoadForOwnerAsync(
                            placeholderDbCtx,
                            playerId.Value,
                            RoomId.Value,
                            ct
                        ),
                    },
                ],
            };
        }

        if (wiredLogic is not WiredVariableReference)
        {
            return snapshot;
        }

        // The one context a box cannot assemble on its own: it names variables in rooms this grain
        // is not, so it is attached here rather than in GetWiredContextSnapshots(), which is
        // synchronous and has no database. Attached unconditionally, empty list included — the
        // client reads a missing section as "reference variables are unavailable" and greys the
        // whole dialog out, which is what every one of these boxes did until now.
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        return snapshot with
        {
            ContextSnapshots =
            [
                .. snapshot.ContextSnapshots,
                new WiredVariableSharedListSnapshot
                {
                    ContextType = WiredContextType.SharedVariables,
                    Elements = await WiredSharedVariableIndex.LoadForOwnerAsync(
                        dbCtx,
                        playerId.Value,
                        ct
                    ),
                },
            ],
        };
    }

    public Task<WiredVariablesSnapshot> GetWiredVariablesSnapshotAsync(CancellationToken ct) =>
        WiredSystem.GetWiredVariablesSnapshotAsync(ct);

    public Task<
        List<(WiredVariableId id, WiredVariableValue value)>
    > GetAllVariablesForBindingAsync(WiredVariableBinding binding, CancellationToken ct) =>
        WiredSystem.GetAllVariablesForBindingAsync(binding, ct);

    public Task<(
        WiredVariableSnapshot Variable,
        List<(int ObjectId, int Value)> Holders
    )?> GetVariableHoldersByNameAsync(string variableName, CancellationToken ct) =>
        WiredSystem.GetVariableHoldersByNameAsync(variableName, ct);
}

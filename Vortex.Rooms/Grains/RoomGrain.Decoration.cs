using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Snapshots.StuffData;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;

namespace Vortex.Rooms.Grains;

/// <summary>
/// Room decorations: the client sends RequestRoomPropertySet with the inventory item id when a
/// category 2/3/4 item is "placed" (AS3 <c>FurniModel.as</c>), and the item is consumed into the
/// room's wallpaper, floor or landscape instead of being put down as furni. That packet has no
/// handler since the cut of 2026-10-07, so nothing calls this yet.
/// </summary>
public sealed partial class RoomGrain
{
    public async Task<bool> ApplyDecorationAsync(
        ActionContext ctx,
        FurnitureItemSnapshot item,
        CancellationToken ct
    )
    {
        string? key = DecorationPropertyKey(item.Definition.FurniCategory);
        string code = (item.StuffData as LegacyStuffSnapshot)?.Data ?? string.Empty;

        // "0" is LegacyStuffData's default, not a pattern: an item bought before decorations kept
        // their code reads back as "0", and applying it consumed the item and painted nothing (the
        // client treats "0" as "no pattern"). Refuse it and leave the item where it is.
        if (
            key is null
            || string.IsNullOrWhiteSpace(code)
            || code == "0"
            || item.OwnerId.Value != ctx.PlayerId.Value
        )
        {
            return false;
        }

        if (!await SecurityModule.CanPlaceFurniAsync(ctx).ConfigureAwait(true))
        {
            return false;
        }

        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            RoomEntity? room = await dbCtx
                .Rooms.FirstOrDefaultAsync(r => r.Id == _state.RoomId.Value, ct)
                .ConfigureAwait(true);

            // Free-standing, still owned by the actor: a stale or forged id deletes nothing.
            FurnitureEntity? furniture = await dbCtx
                .Furnitures.FirstOrDefaultAsync(
                    f =>
                        f.Id == item.ItemId.Value
                        && f.PlayerEntityId == ctx.PlayerId.Value
                        && f.RoomEntityId == null
                        && f.DeletedAt == null,
                    ct
                )
                .ConfigureAwait(true);

            if (room is null || furniture is null)
            {
                return false;
            }

            switch (key)
            {
                case RoomPropertyType.WALLPAPER:
                    room.PaintWall = code;
                    break;
                case RoomPropertyType.FLOOR:
                    room.PaintFloor = code;
                    break;
                default:
                    room.PaintLandscape = code;
                    break;
            }

            furniture.DeletedAt = DateTime.UtcNow;

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

            _state.RoomSnapshot = _state.RoomSnapshot with
            {
                PaintWall = room.PaintWall ?? string.Empty,
                PaintFloor = room.PaintFloor ?? string.Empty,
                PaintLandscape = room.PaintLandscape ?? string.Empty,
                LastUpdatedUtc = DateTime.UtcNow,
            };
            _state.RoomProperties[key] = code;

            await SendComposerToRoomAsync(
                    new RoomPropertyMessageComposer { Key = key, Value = code }
                )
                .ConfigureAwait(true);

            PublishDetached(new RoomDecoratedEvent(ctx.PlayerId.Value, _state.RoomId.Value, key));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to apply decoration {ItemId} to room {RoomId}.",
                item.ItemId,
                _state.RoomId
            );

            return false;
        }
    }

    internal static string? DecorationPropertyKey(FurnitureCategory category) =>
        category switch
        {
            FurnitureCategory.WallPaper => RoomPropertyType.WALLPAPER,
            FurnitureCategory.Floor => RoomPropertyType.FLOOR,
            FurnitureCategory.Landscape => RoomPropertyType.LANDSCAPE,
            _ => null,
        };
}

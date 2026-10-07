using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;
using Vortex.Primitives.Rooms.Events.Zone;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.World.Layers;
using Vortex.Rooms.Grains.Modules;
using Vortex.Rooms.Grains.Zones.Interactions;

namespace Vortex.Rooms.Grains.Zones;

/// <summary>
/// Using a zone furni (plan D10): an Interact event every time, then the effect its
/// <c>interaction</c> names, for any player; without one the furni's own logic answers, rights and all.
/// </summary>
public sealed class ZoneInteractionModule(RoomGrain roomGrain)
{
    private static readonly Dictionary<string, IZoneInteraction> Effects = new IZoneInteraction[]
    {
        new ZoneDoorInteraction(),
        new ZoneLeverInteraction(),
        new ZoneSignInteraction(),
        new ZoneTeleportInteraction(),
        new ZoneSitInteraction(),
        new ZoneUseInteraction(),
    }.ToDictionary(e => e.Kind);

    /// <summary>What is worked by hand, from next to it; a sign is read and a seat walked to anyway.</summary>
    private static readonly HashSet<string> NeedsReach = ["door", "lever", "teleport", "use"];

    /// <summary>Whether the zone handled the use; false hands it to the furni's own logic.</summary>
    public async Task<bool> TryUseAsync(
        ActionContext ctx,
        IRoomItem item,
        int param,
        CancellationToken ct
    )
    {
        if (
            roomGrain._state.ZoneRuntime is not { } zone
            || item is not IRoomFloorItem floorItem
            || zone.Compiled.FloorDecor(item.ObjectId.Value) is not { } decor
        )
        {
            return false;
        }

        await roomGrain.EventModule.PublishAsync(
            new ZoneDecorUsedEvent
            {
                RoomId = roomGrain.RoomId,
                CausedBy = ctx,
                PlayerId = ctx.PlayerId,
                DecorId = decor.Id,
            },
            ct
        );

        if (
            decor.Interaction is not { Length: > 0 } kind
            || !Effects.TryGetValue(kind, out IZoneInteraction? effect)
        )
        {
            return false;
        }

        IRoomPlayer? player =
            roomGrain._state.AvatarsByPlayerId.TryGetValue(ctx.PlayerId, out RoomObjectId objectId)
            && roomGrain._state.AvatarsByObjectId.TryGetValue(objectId, out IRoomAvatar? avatar)
                ? avatar as IRoomPlayer
                : null;

        ZoneUse use = new(roomGrain, ctx, player, floorItem, decor, param);

        if (!NeedsReach.Contains(kind) || InReach(player, floorItem))
        {
            await effect.UseAsync(use, ct);

            return true;
        }

        // Out of reach: walk up to it first, and only use it from there -- a door, a lever or a
        // teleporter is not worked from across the map.
        if (player is not null && NearestApproach(player, floorItem) is { } approach)
        {
            await roomGrain.AvatarModule.WalkAvatarToThenAsync(
                player,
                approach.X,
                approach.Y,
                next =>
                    InReach(player, floorItem) ? effect.UseAsync(use, next) : Task.CompletedTask,
                ct
            );
        }

        return true;
    }

    /// <summary>On the furni's footprint or a tile next to it, diagonals included.</summary>
    private bool InReach(IRoomPlayer? player, IRoomFloorItem item) =>
        player is not null
        && Footprint(item)
            .Any(t => Math.Max(Math.Abs(t.X - player.X), Math.Abs(t.Y - player.Y)) <= 1);

    /// <summary>The tile next to the footprint, that can be stood on, nearest the player.</summary>
    private (int X, int Y)? NearestApproach(IRoomPlayer player, IRoomFloorItem item)
    {
        (int X, int Y)[] footprint = Footprint(item);
        RoomMapModule map = roomGrain.MapModule;

        return footprint
            .SelectMany(t =>
                Enumerable
                    .Range(-1, 3)
                    .SelectMany(dx =>
                        Enumerable.Range(-1, 3).Select(dy => (X: t.X + dx, Y: t.Y + dy))
                    )
            )
            .Where(c =>
                !footprint.Contains(c)
                && map.InBounds(c.X, c.Y)
                && map.IsSpawnable(map.ToIdx(c.X, c.Y))
            )
            .OrderBy(c => Math.Abs(c.X - player.X) + Math.Abs(c.Y - player.Y))
            .Select(c => ((int X, int Y)?)c)
            .FirstOrDefault();
    }

    private (int X, int Y)[] Footprint(IRoomFloorItem item) =>
        roomGrain.MapModule.Footprint(item) is { Count: > 0 } tiles
            ? [.. tiles.Select(roomGrain.MapModule.GetTileXY)]
            : [(item.X, item.Y)];
}

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

        await effect.UseAsync(new ZoneUse(roomGrain, ctx, player, floorItem, decor, param), ct);

        return true;
    }
}

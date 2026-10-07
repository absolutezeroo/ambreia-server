using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.World.Rules;
using Vortex.Rooms.Grains.Zones.Interactions;

namespace Vortex.Rooms.Grains.Zones.Triggers.Actions;

/// <summary>Opens the named "door" furni if it is shut.</summary>
public sealed class OpenDoorExecutor : ZoneActionExecutor<OpenDoorAction>
{
    protected override async Task RunAsync(
        OpenDoorAction action,
        ZoneTriggerContext ctx,
        CancellationToken ct
    )
    {
        if (
            ctx.Zone.Compiled.DecorObjectId(action.Decor) is { } id
            && ctx.Room._state.ItemsById.TryGetValue(new RoomObjectId(id), out IRoomItem? item)
            && item is IRoomFloorItem door
        )
        {
            await ZoneDoorInteraction.SetOpenAsync(ctx.Room, door, true);
        }
    }
}

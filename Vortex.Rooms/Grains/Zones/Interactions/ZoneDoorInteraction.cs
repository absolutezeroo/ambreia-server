using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Rooms.Grains.Zones.Runtime;

namespace Vortex.Rooms.Grains.Zones.Interactions;

/// <summary>Opens or closes: state 1 is open and walkable. Never closes on someone standing in it.</summary>
public sealed class ZoneDoorInteraction : IZoneInteraction
{
    public string Kind => "door";

    public Task UseAsync(ZoneUse use, CancellationToken ct) =>
        SetOpenAsync(
            use.Room,
            use.Item,
            !use.Room._state.ZoneRuntime!.Doors.GetValueOrDefault(use.Item.ObjectId.Value)
        );

    /// <summary>Opens or shuts a door furni (an OpenDoor action too); nothing if it already is.</summary>
    public static async Task SetOpenAsync(RoomGrain room, IRoomFloorItem door, bool open)
    {
        ZoneRuntimeState zone = room._state.ZoneRuntime!;
        int id = door.ObjectId.Value;

        if (
            !zone.Doors.TryGetValue(id, out bool isOpen)
            || isOpen == open
            || !open
                && room.MapModule.Footprint(door)
                    .Any(idx => room._state.TileFlags[idx].Has(RoomTileFlags.AvatarOccupied))
        )
        {
            return;
        }

        // The state first: if it fails, the door is still what the map says it is.
        await door.Logic.SetStateAsync(open ? 1 : 0);

        zone.Doors[id] = open;
        room.MapModule.RecomputeFootprint(door);
    }
}

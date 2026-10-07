using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Rooms.Grains.Zones.Interactions;

/// <summary>Opens or closes: state 1 is open and walkable. Never closes on someone standing in it.</summary>
public sealed class ZoneDoorInteraction : IZoneInteraction
{
    public string Kind => "door";

    public async Task UseAsync(ZoneUse use, CancellationToken ct)
    {
        ZoneRuntimeState zone = use.Room._state.ZoneRuntime!;
        int id = use.Item.ObjectId.Value;
        bool open = !zone.Doors.GetValueOrDefault(id);

        if (
            !open
            && use.Room._state.TileFlags[use.Room.MapModule.ToIdx(use.Item.X, use.Item.Y)]
                .Has(RoomTileFlags.AvatarOccupied)
        )
        {
            return;
        }

        zone.Doors[id] = open;

        await use.Item.Logic.SetStateAsync(open ? 1 : 0);

        use.Room.MapModule.RecomputeFootprint(use.Item);
    }
}

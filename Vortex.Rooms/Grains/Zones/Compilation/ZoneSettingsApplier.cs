using System;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.World;

namespace Vortex.Rooms.Grains.Zones.Compilation;

/// <summary>
/// A zone's settings become its room's (plan D1, D2): name, region as description, capacity as the
/// player limit the entry check reads, and the walls. In memory only: the rooms row stays as it is.
/// </summary>
public static class ZoneSettingsApplier
{
    public static RoomSnapshot Apply(RoomSnapshot room, ZoneDocument zone) =>
        room with
        {
            Name = zone.Settings.Name,
            Description = zone.Settings.Region,
            PlayersMax = zone.Settings.Capacity,
            HideWalls = !zone.Walls.Visible,
            // 0 is "as high as the floor needs", the client's -1.
            WallHeight = zone.Walls.Height > 0 ? zone.Walls.Height : -1,
            // ponytail: the editor offers 0..3 and the client knows Normal (0) and Thick (1) above
            // the default; anything thicker is Thick.
            WallThickness = (RoomThicknessType)Math.Clamp(zone.Walls.Thickness, 0, 1),
        };
}

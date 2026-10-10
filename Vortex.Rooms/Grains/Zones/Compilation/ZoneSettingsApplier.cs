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
            WallThickness = Thickness(zone.Walls.Thickness),
            FloorThickness = Thickness(zone.Floor.Thickness),
        };

    /// <summary>Habbo's scale, -2 (thinnest) to 1 (thick); out of it, the nearest end.</summary>
    private static RoomThicknessType Thickness(int value) =>
        (RoomThicknessType)
            Math.Clamp(value, (int)RoomThicknessType.Thinnest, (int)RoomThicknessType.Thick);
}

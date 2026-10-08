using System;

namespace Vortex.Primitives.Rooms.Enums;

[Flags]
public enum RoomTileFlags : ushort
{
    None = 0,
    Disabled = 1 << 0,
    Open = 1 << 1,
    Closed = 1 << 2,
    StackBlocked = 1 << 3,
    Walkable = 1 << 4,
    Sittable = 1 << 5,
    Layable = 1 << 6,
    AvatarOccupied = 1 << 7,
    FurnitureOccupied = 1 << 8,
    TileClickListener = 1 << 10,
}

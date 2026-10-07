using Vortex.Primitives.Rooms;

namespace Vortex.Primitives.World;

/// <summary>
/// The room ids of dungeon copies (plan D20): <c>Base + template * SlotsPerZone + slot</c>. A copy
/// has no row of its own; its id names the template room whose row, zone and entry log it uses.
/// </summary>
public static class ZoneInstanceIds
{
    /// <summary>Above every real room id: rows are counted from 1 and stay far below.</summary>
    public const int Base = 1_000_000_000;

    /// <summary>Copies one dungeon can have at once, slot 0 unused.</summary>
    public const int SlotsPerZone = 1000;

    /// <summary>The highest template room id that still has room for its copies in an int.</summary>
    public const int MaxTemplateId = (int.MaxValue - Base) / SlotsPerZone - 1;

    public static bool IsInstance(RoomId roomId) => roomId.Value >= Base;

    /// <summary>The room a copy copies; an ordinary room is its own template.</summary>
    public static RoomId TemplateOf(RoomId roomId) =>
        IsInstance(roomId) ? new RoomId((roomId.Value - Base) / SlotsPerZone) : roomId;

    /// <summary>The id of copy <paramref name="slot"/> of a template; null outside the id space.</summary>
    public static RoomId? Of(RoomId template, int slot) =>
        template.Value is > 0 and <= MaxTemplateId && slot is > 0 and < SlotsPerZone
            ? new RoomId(Base + template.Value * SlotsPerZone + slot)
            : null;
}

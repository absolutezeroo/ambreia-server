using Vortex.Primitives.Rooms.Events.Player;

namespace Vortex.Primitives.Rooms.Events.Zone;

/// <summary>A player stepped out of a zone area. Leaving the room is not leaving an area.</summary>
public sealed record ZoneAreaLeftEvent : PlayerEvent
{
    public required string AreaId { get; init; }
}

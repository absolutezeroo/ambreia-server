using Vortex.Primitives.Rooms.Events.Player;

namespace Vortex.Primitives.Rooms.Events.Zone;

/// <summary>A player stepped into a zone area (on arrival too).</summary>
public sealed record ZoneAreaEnteredEvent : PlayerEvent
{
    public required string AreaId { get; init; }
}

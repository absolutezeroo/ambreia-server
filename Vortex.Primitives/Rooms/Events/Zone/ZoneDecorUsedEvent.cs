using Vortex.Primitives.Rooms.Events.Player;

namespace Vortex.Primitives.Rooms.Events.Zone;

/// <summary>A player used a zone furni, whatever its interaction (the Interact trigger, D11).</summary>
public sealed record ZoneDecorUsedEvent : PlayerEvent
{
    public required string DecorId { get; init; }
}

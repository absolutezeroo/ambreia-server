using Vortex.Primitives.Rooms.Events.Player;
using Vortex.Primitives.World.Layers;

namespace Vortex.Primitives.Rooms.Events.Zone;

/// <summary>
/// Something a module saw happen to a player sets off the zone's triggers of this type and target
/// (spec Z1 §5): a level reached, an item gained, a monster killed. The room does not know what it
/// means; it only runs the triggers.
/// </summary>
public sealed record ZoneTriggerRaisedEvent : PlayerEvent
{
    public required ZoneTriggerType Type { get; init; }

    public required string Target { get; init; }
}

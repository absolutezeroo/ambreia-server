using Vortex.Primitives.World.Layers;

namespace Vortex.Rpg.Zones.Rules;

/// <summary>Something a module saw happen to a player that the zone's triggers of this type and
/// target listen to (spec Z1 §5): a level reached, an item gained.</summary>
public sealed record ZoneTriggerSignal(ZoneTriggerType Type, string Target);

using System.Collections.Generic;
using Vortex.Primitives.World.Layers;

namespace Vortex.Rooms.Grains.Zones;

/// <summary>
/// A player in the zone, from their arrival step to their exit: the areas they stand in. What must
/// outlive the visit (cooldowns) is the zone's, in <see cref="ZoneCooldowns"/>.
/// </summary>
public sealed class ZoneVisitor
{
    public IReadOnlyList<ZoneArea> Areas { get; set; } = [];
}

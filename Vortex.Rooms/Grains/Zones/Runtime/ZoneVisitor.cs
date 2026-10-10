using System.Collections.Generic;
using Vortex.Primitives.World.Layers;

namespace Vortex.Rooms.Grains.Zones.Runtime;

/// <summary>
/// A player in the zone, from their arrival step to their exit: the areas they stand in, and where
/// they were told the recommended level. What must outlive the visit (cooldowns) is the zone's, in
/// <see cref="ZoneCooldowns"/>.
/// </summary>
public sealed class ZoneVisitor
{
    public IReadOnlyList<ZoneArea> Areas { get; set; } = [];

    /// <summary>The zone ("zone") and the areas (their ids) whose level advice was given this visit.</summary>
    public HashSet<string> Advised { get; } = [];
}

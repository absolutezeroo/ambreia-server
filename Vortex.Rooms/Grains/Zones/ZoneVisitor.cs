using System.Collections.Generic;
using Vortex.Primitives.World.Layers;

namespace Vortex.Rooms.Grains.Zones;

/// <summary>A player in the zone: the areas they stand in and when each passage opens again for them.</summary>
public sealed class ZoneVisitor
{
    public IReadOnlyList<ZoneArea> Areas { get; set; } = [];

    public Dictionary<string, long> PassageReadyAtMs { get; } = [];

    /// <summary>When each trigger fires again for this player (plan D14).</summary>
    public Dictionary<string, long> TriggerReadyAtMs { get; } = [];
}

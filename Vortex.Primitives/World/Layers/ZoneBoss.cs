using System.Collections.Generic;

namespace Vortex.Primitives.World.Layers;

public sealed record ZoneBoss
{
    public required string Id { get; init; }
    public required List<ZoneCell> Arena { get; init; }
    public required string Encounter { get; init; }
    public required ZoneCell Spawn { get; init; }
    public List<ZoneCell> Adds { get; init; } = [];
    public List<string> Doors { get; init; } = [];
}

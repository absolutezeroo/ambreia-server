using System.Collections.Generic;
using Vortex.Primitives.World.Rules;

namespace Vortex.Primitives.World.Layers;

public sealed record ZoneArea
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public required List<ZoneCell> Polygon { get; init; }
    public int Priority { get; init; }
    public string Music { get; init; } = string.Empty;
    public int RecommendedLevel { get; init; }
    public List<string> Tags { get; init; } = [];
    public List<ZoneCondition> Conditions { get; init; } = [];
}

using System.Collections.Generic;
using Vortex.Primitives.World.Rules;

namespace Vortex.Primitives.World.Layers;

public sealed record ZonePassage
{
    public required string Id { get; init; }
    public required List<ZoneCell> Area { get; init; }
    public required string DestinationZone { get; init; }
    public required string DestinationEntry { get; init; }
    public bool Fade { get; init; } = true;
    public int Cost { get; init; }
    public int CooldownSeconds { get; init; }
    public List<ZoneCondition> Conditions { get; init; } = [];
}

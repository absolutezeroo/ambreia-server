using System.Collections.Generic;
using Vortex.Primitives.World.Rules;

namespace Vortex.Primitives.World.Layers;

public sealed record ZoneChest
{
    public required string Id { get; init; }
    public required string Decor { get; init; }
    public required string LootTable { get; init; }
    public int RespawnSeconds { get; init; }
    public bool PerPlayer { get; init; }
    public List<ZoneCondition> Conditions { get; init; } = [];
}

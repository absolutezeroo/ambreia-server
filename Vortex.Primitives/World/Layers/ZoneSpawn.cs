using System.Collections.Generic;
using Vortex.Primitives.World.Rules;

namespace Vortex.Primitives.World.Layers;

public sealed record ZoneSpawn
{
    public required string Id { get; init; }
    public required List<ZoneCell> Area { get; init; }
    public required string Monster { get; init; }
    public int Min { get; init; } = 1;
    public int Max { get; init; } = 1;
    public int RespawnSeconds { get; init; } = 30;
    public int WanderRadius { get; init; } = 3;
    public int AggroRadius { get; init; } = 5;
    public int LevelMin { get; init; } = 1;
    public int LevelMax { get; init; } = 1;
    public bool AoiActivation { get; init; } = true;
    public string? Group { get; init; }
    public List<ZoneCondition> Conditions { get; init; } = [];
}

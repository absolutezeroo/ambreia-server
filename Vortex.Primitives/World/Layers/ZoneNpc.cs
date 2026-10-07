using System.Collections.Generic;
using Vortex.Primitives.World.Rules;

namespace Vortex.Primitives.World.Layers;

public sealed record ZoneNpc
{
    public required string Id { get; init; }
    public required string Template { get; init; }
    public string? Name { get; init; }
    public string? Look { get; init; }
    public required ZoneCell Cell { get; init; }
    public int Rotation { get; init; } = 2;
    public List<string> Interactions { get; init; } = [];
    public string? Animation { get; init; }
    public string? Icon { get; init; }

    /// <summary>The dialogue the NPC opens when talked to (an id; the dialogue system is RPG).</summary>
    public string? Dialogue { get; init; }
    public List<ZoneCondition> AppearWhen { get; init; } = [];
}

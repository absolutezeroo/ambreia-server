using System.Collections.Generic;
using Vortex.Primitives.World.Rules;

namespace Vortex.Primitives.World.Layers;

public sealed record ZoneTrigger
{
    public required string Id { get; init; }
    public required ZoneTriggerType Type { get; init; }
    public string? Target { get; init; }
    public List<ZoneCondition> Conditions { get; init; } = [];
    public List<ZoneAction> Actions { get; init; } = [];
    public int CooldownMs { get; init; }
    public bool Active { get; init; } = true;
    public bool VisibleInDebug { get; init; } = true;
}

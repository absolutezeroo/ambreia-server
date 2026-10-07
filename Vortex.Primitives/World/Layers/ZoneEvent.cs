using System.Collections.Generic;

namespace Vortex.Primitives.World.Layers;

public sealed record ZoneEvent
{
    public required string Id { get; init; }
    public required List<ZoneCell> Area { get; init; }
    public required string Definition { get; init; }
    public int DurationSeconds { get; init; }
    public int CooldownSeconds { get; init; }
}

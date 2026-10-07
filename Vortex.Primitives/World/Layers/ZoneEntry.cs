namespace Vortex.Primitives.World.Layers;

public sealed record ZoneEntry
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required ZoneCell Cell { get; init; }
    public int Direction { get; init; } = 2;
}

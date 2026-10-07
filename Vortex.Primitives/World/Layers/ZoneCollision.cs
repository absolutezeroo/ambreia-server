namespace Vortex.Primitives.World.Layers;

public sealed record ZoneCollision
{
    public bool? Walkable { get; init; }
    public bool? BlocksSight { get; init; }
    public double? Height { get; init; }
    public bool? Seat { get; init; }
}

namespace Vortex.Primitives.World.Layers;

public sealed record ZoneDecorObject
{
    public required string Id { get; init; }
    public required int Definition { get; init; }
    public required string Class { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public double Z { get; init; }
    public int Rotation { get; init; }
    public int State { get; init; }
    public ZoneCollision? Collision { get; init; }
    public string? Interaction { get; init; }
    public string? Tag { get; init; }
    public bool Locked { get; init; }
}

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

    /// <summary>Ignored at run time (spec F3b §4): a zone's decor is always frozen. Kept so stored
    /// zone versions that carry it still parse — <c>ZoneJson</c> refuses unknown fields.</summary>
    public bool Locked { get; init; }
}

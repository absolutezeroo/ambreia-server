namespace Vortex.Primitives.World.Layers;

public sealed record ZoneTile
{
    public int Height { get; init; }
    public bool Walkable { get; init; } = true;
    public bool Hole { get; init; }
    public string? Zone { get; init; }
    public string? Note { get; init; }
}

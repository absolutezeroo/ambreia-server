namespace Vortex.Primitives.World.Layers;

public sealed record ZoneWalls
{
    public bool Visible { get; init; } = true;
    public int Height { get; init; }
    public int Thickness { get; init; }
}

namespace Vortex.Primitives.World.Layers;

public sealed record ZoneWallDecorObject
{
    public required string Id { get; init; }
    public required int Definition { get; init; }
    public required string Class { get; init; }
    public required ZoneWallSide Side { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public int OffsetX { get; init; }
    public int OffsetY { get; init; }
    public int State { get; init; }
}

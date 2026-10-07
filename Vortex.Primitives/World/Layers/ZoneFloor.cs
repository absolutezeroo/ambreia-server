using System.Collections.Generic;

namespace Vortex.Primitives.World.Layers;

public sealed record ZoneFloor
{
    public required int Width { get; init; }
    public required int Length { get; init; }

    /// <summary>Row-major, <c>Width × Length</c> cells; index = y * Width + x.</summary>
    public required List<ZoneTile> Tiles { get; init; }
    public required ZoneCell Door { get; init; }
    public int DoorDirection { get; init; } = 2;
}

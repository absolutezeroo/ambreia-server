namespace Vortex.Primitives.World.Layers;

public sealed record ZoneInteractive
{
    public required string Id { get; init; }
    public required string Decor { get; init; }
    public required string Interaction { get; init; }
}

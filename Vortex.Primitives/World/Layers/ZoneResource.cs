namespace Vortex.Primitives.World.Layers;

public sealed record ZoneResource
{
    public required string Id { get; init; }
    public string? Decor { get; init; }
    public ZoneCell? Cell { get; init; }
    public required string Template { get; init; }
    public string? Profession { get; init; }
    public int Level { get; init; }
    public int RespawnSeconds { get; init; } = 60;
}

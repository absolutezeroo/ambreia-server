namespace Vortex.Primitives.World.Layers;

public sealed record ZoneSettings
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Region { get; init; } = string.Empty;
    public ZoneType Type { get; init; } = ZoneType.World;
    public int Capacity { get; init; } = 50;
    public int RecommendedLevel { get; init; } = 1;
    public bool PvpAllowed { get; init; }
    public string Music { get; init; } = string.Empty;
    public string Weather { get; init; } = string.Empty;
    public bool Template { get; init; }
}

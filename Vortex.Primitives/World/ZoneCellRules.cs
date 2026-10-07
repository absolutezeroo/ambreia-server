using Orleans;
using Vortex.Primitives.World.Layers;

namespace Vortex.Primitives.World;

/// <summary>
/// What one cell of a zone allows (plan D24): what combat, monsters and mounts ask before they act.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record ZoneCellRules
{
    /// <summary>No combat at all: no PvP, no monster attacks.</summary>
    [Id(0)]
    public required bool Safe { get; init; }

    [Id(1)]
    public required bool Pvp { get; init; }

    /// <summary>Monsters attack on sight.</summary>
    [Id(2)]
    public required bool Hostile { get; init; }

    [Id(3)]
    public required bool NoMount { get; init; }

    /// <summary>Only players who meet the area's conditions may step in.</summary>
    [Id(4)]
    public required bool Restricted { get; init; }

    [Id(5)]
    public required int RecommendedLevel { get; init; }

    /// <summary>The area's music, else the zone's; empty for none.</summary>
    [Id(6)]
    public required string Music { get; init; }

    [Id(7)]
    public required ZoneType Type { get; init; }
}

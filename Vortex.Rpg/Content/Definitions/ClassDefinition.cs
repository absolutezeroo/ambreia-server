using System.Collections.Generic;

namespace Vortex.Rpg.Content.Definitions;

/// <summary>A playable class (spec Personnage §4): its role, reach, stats at level 1 and growth per level.</summary>
public sealed record ClassDefinition : ContentDefinition
{
    public required ClassRole Role { get; init; }

    /// <summary>Attack range in tiles.</summary>
    public required int Range { get; init; }

    public int ThreatMultiplier { get; init; } = 1;

    public required PrimaryStat PrimaryStat { get; init; }

    /// <summary>Stats at level 1.</summary>
    public required StatBlock Base { get; init; }

    /// <summary>Added for each level above 1.</summary>
    public required StatBlock PerLevel { get; init; }

    public List<string> Specializations { get; init; } = [];
}

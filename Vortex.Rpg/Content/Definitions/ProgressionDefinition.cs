using System.Collections.Generic;

namespace Vortex.Rpg.Content.Definitions;

/// <summary>The XP curve and the coefficients of the derived stats (spec Personnage §5, §6.1).</summary>
public sealed record ProgressionDefinition : ContentDefinition
{
    public required int MaxLevel { get; init; }

    /// <summary>XP to go from level n to n + 1, for n = 1 … MaxLevel − 1.</summary>
    public required List<int> XpToNext { get; init; }

    public required int HealthPerStrength { get; init; }

    public required int ManaPerIntelligence { get; init; }

    public required double CritBasePercent { get; init; }

    public required double CritPerAgilityPercent { get; init; }

    public required double CritCapPercent { get; init; }

    public required double DodgePerAgilityPercent { get; init; }

    public required double DodgeCapPercent { get; init; }
}

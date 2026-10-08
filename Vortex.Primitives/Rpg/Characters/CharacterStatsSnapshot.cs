using System.Collections.Immutable;
using Orleans;

namespace Vortex.Primitives.Rpg.Characters;

/// <summary>
/// A character's final stats and where they came from (spec Personnage §6.2). Kept by the character
/// grain and asked for once when the character enters a zone.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record CharacterStatsSnapshot
{
    [Id(0)]
    public int Strength { get; init; }

    [Id(1)]
    public int Intelligence { get; init; }

    [Id(2)]
    public int Agility { get; init; }

    [Id(3)]
    public int MaxHealth { get; init; }

    [Id(4)]
    public int MaxMana { get; init; }

    [Id(5)]
    public int Armor { get; init; }

    [Id(6)]
    public double CritPercent { get; init; }

    [Id(7)]
    public double DodgePercent { get; init; }

    /// <summary>100 is normal speed; bounded to [50, 200].</summary>
    [Id(8)]
    public int SpeedPercent { get; init; }

    /// <summary>The class's primary stat, final value.</summary>
    [Id(9)]
    public int Power { get; init; }

    [Id(10)]
    public ImmutableArray<CharacterStatContribution> Sources { get; init; } = [];
}

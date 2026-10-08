using Orleans;

namespace Vortex.Primitives.Rpg.Characters;

/// <summary>What one stat source added (spec Personnage §6.2): the detail the interface and debug show.</summary>
[GenerateSerializer, Immutable]
public sealed record CharacterStatContribution
{
    [Id(0)]
    public required string Source { get; init; }

    [Id(1)]
    public int Strength { get; init; }

    [Id(2)]
    public int Intelligence { get; init; }

    [Id(3)]
    public int Agility { get; init; }

    [Id(4)]
    public int Health { get; init; }

    [Id(5)]
    public int Mana { get; init; }

    [Id(6)]
    public int Armor { get; init; }

    [Id(7)]
    public int Speed { get; init; }
}

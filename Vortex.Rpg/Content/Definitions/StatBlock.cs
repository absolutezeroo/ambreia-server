namespace Vortex.Rpg.Content.Definitions;

/// <summary>A set of stat values; an absent stat is 0 (spec Personnage §4, spec Objets §4).</summary>
public sealed record StatBlock
{
    public int Strength { get; init; }

    public int Intelligence { get; init; }

    public int Agility { get; init; }

    public int Health { get; init; }

    public int Mana { get; init; }

    public int Armor { get; init; }

    /// <summary>A percentage added to the base speed of 100.</summary>
    public int Speed { get; init; }
}

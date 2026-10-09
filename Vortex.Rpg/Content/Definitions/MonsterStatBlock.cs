namespace Vortex.Rpg.Content.Definitions;

/// <summary>A monster's stats at level 1, or what each level above adds (spec M1a §5.1).</summary>
public sealed record MonsterStatBlock
{
    public int Health { get; init; }

    public int Armor { get; init; }

    public int DamageMin { get; init; }

    public int DamageMax { get; init; }

    public double CritPercent { get; init; }

    public double DodgePercent { get; init; }
}

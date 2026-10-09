using Vortex.Rpg.Combat.Hits;

namespace Vortex.Rpg.Combat.Tests.Support;

/// <summary>A fighter's numbers, for the hit chain tests.</summary>
internal sealed record TestStats : ICombatantStats
{
    public bool IsPlayer { get; init; }

    public int Power { get; init; }

    public int DamageMin { get; init; }

    public int DamageMax { get; init; }

    public int Armor { get; init; }

    public double CritPercent { get; init; }

    public double DodgePercent { get; init; }
}

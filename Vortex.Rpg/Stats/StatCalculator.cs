using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Stats;

/// <summary>
/// Spec Personnage §6.1: final stat = sum of the sources, then the derived stats from the final
/// primary ones. Pure and deterministic; no stat is ever negative.
/// </summary>
public static class StatCalculator
{
    private const int NormalSpeed = 100;
    private const int MinSpeed = 50;
    private const int MaxSpeed = 200;

    public static CharacterStatsSnapshot Compute(
        IReadOnlyList<(string Source, StatBlock Stats)> contributions,
        ProgressionDefinition? progression,
        PrimaryStat? primary
    )
    {
        int Sum(Func<StatBlock, int> field) => contributions.Sum(c => field(c.Stats));

        int strength = Math.Max(0, Sum(s => s.Strength));
        int intelligence = Math.Max(0, Sum(s => s.Intelligence));
        int agility = Math.Max(0, Sum(s => s.Agility));

        double healthPerStrength = progression?.HealthPerStrength ?? 0;
        double manaPerIntelligence = progression?.ManaPerIntelligence ?? 0;

        return new CharacterStatsSnapshot
        {
            Strength = strength,
            Intelligence = intelligence,
            Agility = agility,
            MaxHealth = Math.Max(0, (int)(Sum(s => s.Health) + strength * healthPerStrength)),
            MaxMana = Math.Max(0, (int)(Sum(s => s.Mana) + intelligence * manaPerIntelligence)),
            Armor = Math.Max(0, Sum(s => s.Armor)),
            CritPercent = Bounded(
                (progression?.CritBasePercent ?? 0)
                    + agility * (progression?.CritPerAgilityPercent ?? 0),
                progression?.CritCapPercent ?? 0
            ),
            DodgePercent = Bounded(
                agility * (progression?.DodgePerAgilityPercent ?? 0),
                progression?.DodgeCapPercent ?? 0
            ),
            SpeedPercent = Math.Clamp(NormalSpeed + Sum(s => s.Speed), MinSpeed, MaxSpeed),
            Power = primary switch
            {
                PrimaryStat.Strength => strength,
                PrimaryStat.Intelligence => intelligence,
                PrimaryStat.Agility => agility,
                _ => 0,
            },
            Sources =
            [
                .. contributions.Select(c => new CharacterStatContribution
                {
                    Source = c.Source,
                    Strength = c.Stats.Strength,
                    Intelligence = c.Stats.Intelligence,
                    Agility = c.Stats.Agility,
                    Health = c.Stats.Health,
                    Mana = c.Stats.Mana,
                    Armor = c.Stats.Armor,
                    Speed = c.Stats.Speed,
                }),
            ],
        };
    }

    private static double Bounded(double value, double cap) =>
        Math.Clamp(value, 0, Math.Max(0, cap));
}

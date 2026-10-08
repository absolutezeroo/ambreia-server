using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Stats;
using Xunit;

namespace Vortex.Rpg.Tests.Stats;

/// <summary>Spec Personnage §6.1: the sum of the sources, then the derived stats, never negative.</summary>
public sealed class StatCalculatorTests
{
    private static readonly ProgressionDefinition Progression = new()
    {
        Id = "progression.default",
        Kind = "progression",
        MaxLevel = 20,
        XpToNext =
        [
            100,
            150,
            220,
            300,
            400,
            520,
            660,
            820,
            1000,
            1200,
            1450,
            1750,
            2100,
            2500,
            2950,
            3450,
            4000,
            4600,
            5300,
        ],
        HealthPerStrength = 5,
        ManaPerIntelligence = 5,
        CritBasePercent = 5,
        CritPerAgilityPercent = 0.2,
        CritCapPercent = 50,
        DodgePerAgilityPercent = 0.1,
        DodgeCapPercent = 30,
    };

    // content/class/warrior.json
    private static readonly StatBlock WarriorBase = new()
    {
        Strength = 12,
        Intelligence = 4,
        Agility = 6,
        Health = 60,
        Mana = 40,
        Armor = 4,
    };

    private static readonly StatBlock WarriorGrowth = new()
    {
        Strength = 3,
        Agility = 1,
        Health = 18,
        Mana = 2,
        Armor = 1,
    };

    private static StatBlock ClassAt(StatBlock b, StatBlock g, int level) =>
        new()
        {
            Strength = b.Strength + g.Strength * (level - 1),
            Intelligence = b.Intelligence + g.Intelligence * (level - 1),
            Agility = b.Agility + g.Agility * (level - 1),
            Health = b.Health + g.Health * (level - 1),
            Mana = b.Mana + g.Mana * (level - 1),
            Armor = b.Armor + g.Armor * (level - 1),
            Speed = b.Speed + g.Speed * (level - 1),
        };

    private static CharacterStatsSnapshot One(StatBlock stats, PrimaryStat? primary = null) =>
        StatCalculator.Compute([("class", stats)], Progression, primary);

    [Fact]
    public void AWarriorAtLevelOne_HasTheSpecHealth()
    {
        CharacterStatsSnapshot s = One(
            ClassAt(WarriorBase, WarriorGrowth, 1),
            PrimaryStat.Strength
        );

        (s.MaxHealth, s.MaxMana, s.Armor, s.Power).Should().Be((120, 60, 4, 12));
        s.CritPercent.Should().BeApproximately(6.2, 1e-9);
        s.DodgePercent.Should().BeApproximately(0.6, 1e-9);
        s.SpeedPercent.Should().Be(100);
    }

    [Fact]
    public void AWarriorAtLevelTwenty_Grows() =>
        One(ClassAt(WarriorBase, WarriorGrowth, 20), PrimaryStat.Strength)
            .MaxHealth.Should()
            .Be(402 + 69 * 5);

    [Fact]
    public void CritAndDodge_StopAtTheirCaps()
    {
        CharacterStatsSnapshot s = One(new StatBlock { Agility = 1000 });

        (s.CritPercent, s.DodgePercent).Should().Be((50, 30));
    }

    [Fact]
    public void Speed_IsBoundedBetweenFiftyAndTwoHundred() =>
        (
            One(new StatBlock { Speed = -500 }).SpeedPercent,
            One(new StatBlock { Speed = 500 }).SpeedPercent
        )
            .Should()
            .Be((50, 200));

    [Fact]
    public void NoStat_IsNegative()
    {
        CharacterStatsSnapshot s = One(
            new StatBlock
            {
                Strength = -100,
                Intelligence = -100,
                Agility = -100,
                Health = -100,
                Mana = -100,
                Armor = -100,
            },
            PrimaryStat.Agility
        );

        new[] { s.Strength, s.Intelligence, s.Agility, s.MaxHealth, s.MaxMana, s.Armor, s.Power }
            .Should()
            .AllBeEquivalentTo(0);
        (s.CritPercent, s.DodgePercent, s.SpeedPercent).Should().Be((5, 0, 100));
    }

    [Fact]
    public void EverySource_IsKeptInTheDetail()
    {
        CharacterStatsSnapshot s = StatCalculator.Compute(
            [
                ("class", new StatBlock { Strength = 10 }),
                ("equipment", new StatBlock { Strength = 2 }),
            ],
            Progression,
            PrimaryStat.Strength
        );

        s.Strength.Should().Be(12);
        s.Sources.Select(x => (x.Source, x.Strength))
            .Should()
            .Equal(("class", 10), ("equipment", 2));
    }

    [Fact]
    public void WithoutProgression_DerivedCoefficientsAreZero()
    {
        CharacterStatsSnapshot s = StatCalculator.Compute(
            [("class", new StatBlock { Health = 50, Strength = 10 })],
            null,
            null
        );

        (s.MaxHealth, s.CritPercent, s.Power).Should().Be((50, 0, 0));
    }
}

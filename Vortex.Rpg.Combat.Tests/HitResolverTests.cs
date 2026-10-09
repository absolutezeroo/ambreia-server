using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Rpg.Combat.Configuration;
using Vortex.Rpg.Combat.Hits;
using Vortex.Rpg.Combat.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Combat.Tests;

/// <summary>Spec M1a §6.2: a hit goes through declared steps by priority — dodge, base damage, crit,
/// armor, at least 1. The random draws are fixed: each test says which value each step reads.</summary>
public sealed class HitResolverTests
{
    private static readonly IOptions<CombatConfig> Config = Options.Create(new CombatConfig());

    private static readonly TestStats Player = new() { IsPlayer = true, Power = 20 };

    private static readonly TestStats Wolf = new() { DamageMin = 3, DamageMax = 5 };

    private static HitResolver Resolver(params IHitStep[] extra) =>
        new([
            new DodgeStep(),
            new BaseDamageStep(Config),
            new CritStep(Config),
            new ArmorStep(),
            new MinimumStep(),
            .. extra,
        ]);

    [Fact]
    public void ADodge_IsAMiss_WithNoDamage()
    {
        // dodge draw 0.01 × 100 = 1 < 5 %.
        (int amount, string outcome) = Resolver()
            .Resolve(Player, Wolf with { DodgePercent = 5 }, new FixedRandom(0.01));

        (amount, outcome).Should().Be((0, "miss"));
    }

    [Fact]
    public void APlayerHit_IsPowerTimesTheRoll()
    {
        // no dodge (0.99), roll 0.5 → factor 1.0, no crit (0.99).
        (int amount, string outcome) = Resolver()
            .Resolve(Player, Wolf, new FixedRandom(0.99, 0.5, 0.99));

        (amount, outcome).Should().Be((20, "hit"));
    }

    [Theory]
    [InlineData(0.0, 3)]
    [InlineData(0.99, 5)]
    public void AMonsterHit_IsBetweenItsMinAndMax(double roll, int expected)
    {
        (int amount, _) = Resolver().Resolve(Wolf, Player, new FixedRandom(0.99, roll, 0.99));

        amount.Should().Be(expected);
    }

    [Fact]
    public void ACrit_IsOneAndAHalf()
    {
        (int amount, string outcome) = Resolver()
            .Resolve(Player with { CritPercent = 10 }, Wolf, new FixedRandom(0.99, 0.5, 0.05));

        (amount, outcome).Should().Be((30, "crit"));
    }

    [Fact]
    public void Armor_Reduces_AsTheSpecSays()
    {
        (int amount, _) = Resolver()
            .Resolve(Player, Wolf with { Armor = 100 }, new FixedRandom(0.99, 0.5, 0.99));

        amount.Should().Be(10, "20 × 100 / (100 + 100)");
    }

    [Fact]
    public void AHit_IsAtLeastOne()
    {
        (int amount, string outcome) = Resolver()
            .Resolve(
                Player with
                {
                    Power = 1,
                },
                Wolf with
                {
                    Armor = 10_000,
                },
                new FixedRandom(0.99, 0.5, 0.99)
            );

        (amount, outcome).Should().Be((1, "hit"));
    }

    [Fact]
    public void ADeclaredStep_RunsAtItsPriority()
    {
        // Doubling at 350: after the crit (×1.5 → 30), before the armor (/2 → 30).
        (int amount, _) = Resolver(new DoublingStep())
            .Resolve(
                Player with
                {
                    CritPercent = 10,
                },
                Wolf with
                {
                    Armor = 100,
                },
                new FixedRandom(0.99, 0.5, 0.05)
            );

        amount.Should().Be(30);
    }

    private sealed class DoublingStep : IHitStep
    {
        public int Priority => 350;

        public bool Apply(HitContext hit)
        {
            hit.Damage *= 2;

            return true;
        }
    }
}

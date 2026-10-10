using FluentAssertions;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Monsters.Configuration;
using Xunit;

namespace Vortex.Rpg.Monsters.Tests;

/// <summary>Spec M1c §10: the ranks' defaults, and a wrong value named by its key.</summary>
public sealed class MonstersConfigTests
{
    [Fact]
    public void TheDefaults_AreValid() => new MonstersConfig().Problems().Should().BeEmpty();

    [Fact]
    public void ABoss_IgnoresStunsAndSlows_ByDefault()
    {
        RankScale boss = new MonstersConfig().ScaleFor(MonsterRank.Boss);

        (boss.Health, boss.Damage, boss.Xp).Should().Be((10, 2, 10));
        boss.Immunities.Should().BeEquivalentTo([StatusType.Stun, StatusType.Slow]);
    }

    [Fact]
    public void ANormalMonster_IsNotScaled() =>
        new MonstersConfig().ScaleFor(MonsterRank.Normal).Should().Be(RankScale.None);

    [Fact]
    public void AMultiplierAtZero_IsNamedByItsKey()
    {
        MonstersConfig config = new();
        config.Ranks["elite"].HealthMultiplier = 0;

        config
            .Problems()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("Vortex:Monsters:Ranks:elite:HealthMultiplier");
    }

    [Fact]
    public void AnUnknownImmunity_IsNamedByItsKey()
    {
        MonstersConfig config = new();
        config.Ranks["boss"].Immunities.Add("root");

        config.Problems().Should().ContainSingle().Which.Should().Contain("root");
    }

    [Fact]
    public void AnUnknownRank_IsNamedByItsKey()
    {
        MonstersConfig config = new();
        config.Ranks["legendary"] = new MonsterRankConfig();

        config.Problems().Should().ContainSingle().Which.Should().Contain("legendary");
    }
}

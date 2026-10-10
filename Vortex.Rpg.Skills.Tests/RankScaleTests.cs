using System.Collections.Frozen;
using FluentAssertions;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Skills.Tests.Support;
using Vortex.Rpg.Zones;
using Xunit;

namespace Vortex.Rpg.Skills.Tests;

/// <summary>Spec M1c §7.1: a monster's rank scales its health, damage and XP when it comes in, and
/// brings its immunities. The wolf: 40 health, 3–5 damage, 20 XP.</summary>
public sealed class RankScaleTests
{
    private readonly SkillWorld _world = new();

    private Combatant Spawn(RankScale? rank)
    {
        MonsterDefinition d = SkillWorld.WolfDefinition;
        int id = _world.Zone.SpawnMonster(new MonsterAppearance(d.Id, d.Figure, 1), 6, 5);

        return _world.Combat.AddMonster(id, 1, d, rank);
    }

    [Fact]
    public void ARank_ScalesHealthDamageAndXp()
    {
        Combatant elite = Spawn(new RankScale(3, 1.5, 3, FrozenSet.ToFrozenSet([StatusType.Stun])));

        (elite.MaxHealth, elite.Health, elite.DamageMin, elite.DamageMax, elite.Xp)
            .Should()
            .Be((120, 120, 5, 8, 60));
        elite.Immunities.Should().BeEquivalentTo([StatusType.Stun]);
    }

    [Fact]
    public void NoRank_LeavesTheMonsterAsItIs()
    {
        Combatant wolf = Spawn(null);

        (wolf.MaxHealth, wolf.DamageMin, wolf.DamageMax, wolf.Xp).Should().Be((40, 3, 5, 20));
        wolf.Immunities.Should().BeEmpty();
    }

    [Fact]
    public void AScaledHealth_IsNeverBelowOne() =>
        Spawn(new RankScale(0.001, 1, 1, FrozenSet<StatusType>.Empty)).MaxHealth.Should().Be(1);
}

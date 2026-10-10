using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Rpg.Interaction;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Monsters.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Monsters.Tests;

/// <summary>Spec M1c §7.1, §9.3: a monster comes in scaled by its rank, and its rank is on the
/// target frame. The wolf: 40 health.</summary>
public sealed class MonsterRankTests
{
    private readonly MonsterWorld _world = new();

    private async Task<Combatant> SpawnAsync(MonsterRank rank)
    {
        MonsterDefinition wolf = MonsterWorld.Wolf with { Id = "monster.test.wolf", Rank = rank };
        _world.Start([MonsterWorld.WolvesAt(1, (1, 1)) with { Monster = wolf.Id }], null, wolf);
        await _world.EnterAsync(7, 1, 9, 9);
        await _world.TickAsync();

        return _world.Simulation.Combat.Combatants[_world.Zone.Spawned.Single().ObjectId];
    }

    [Fact]
    public async Task AnElite_ComesInWithThreeTimesTheHealth() =>
        (await SpawnAsync(MonsterRank.Elite)).MaxHealth.Should().Be(120);

    [Fact]
    public async Task ABoss_ComesInWithItsImmunities() =>
        (await SpawnAsync(MonsterRank.Boss))
            .Immunities.Should()
            .BeEquivalentTo([StatusType.Stun, StatusType.Slow]);

    [Fact]
    public async Task TheTargetFrame_CarriesTheRank()
    {
        Combatant boss = await SpawnAsync(MonsterRank.Boss);

        await _world.Simulation.SelectTargetAsync(
            new PlayerId(7),
            boss.ObjectId,
            CancellationToken.None
        );

        _world
            .Zone.Sent.Select(s => s.Composer)
            .OfType<RpgTargetMessageComposer>()
            .Should()
            .ContainSingle()
            .Which.Rank.Should()
            .Be(RpgTargetMessageComposer.RankBoss);
    }
}

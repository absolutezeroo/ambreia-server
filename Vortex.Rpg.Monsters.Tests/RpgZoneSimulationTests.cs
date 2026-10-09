using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Protocol.Messages.Outgoing.Rpg.Interaction;
using Vortex.Rpg.Monsters.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Monsters.Tests;

/// <summary>Spec M1a §9.1: a click on a monster targets it and starts the attack; anything else is
/// the zone's.</summary>
public sealed class RpgZoneSimulationTests
{
    private readonly MonsterWorld _world = new();

    private async Task<int> AWolfAsync()
    {
        _world.Start([MonsterWorld.WolvesAt(1, (8, 8))]);
        await _world.EnterAsync(7, 1, 1, 1);
        await _world.TickAsync();

        return _world.Zone.Spawned.Single().ObjectId;
    }

    [Fact]
    public async Task SelectingAMonster_AnswersRpgTarget_AndStartsTheAttack()
    {
        int wolf = await AWolfAsync();

        (await _world.Simulation.SelectTargetAsync(new PlayerId(7), wolf, CancellationToken.None))
            .Should()
            .BeTrue();

        _world
            .Zone.Sent.Select(s => s.Composer)
            .OfType<RpgTargetMessageComposer>()
            .Should()
            .Equal(
                new RpgTargetMessageComposer
                {
                    ObjectId = wolf,
                    Kind = RpgTargetMessageComposer.KindMonster,
                    Name = MonsterWorld.Wolf.Id,
                    Level = 1,
                    ClassId = string.Empty,
                }
            );
        _world.Simulation.Combat.Combatants[1].TargetId.Should().Be(wolf);
    }

    [Fact]
    public async Task SelectingAMonsterFromASafeCell_IsRefused()
    {
        int wolf = await AWolfAsync();
        _world.Zone.SafeCells.Add((1, 1));

        (await _world.Simulation.SelectTargetAsync(new PlayerId(7), wolf, CancellationToken.None))
            .Should()
            .BeTrue();

        _world
            .Zone.Sent.Select(s => s.Composer)
            .OfType<RpgCombatResultMessageComposer>()
            .Single()
            .Code.Should()
            .Be("safe_zone");
    }

    /// <summary>A killed monster's body is no target: the click goes to the room, which clears it.</summary>
    [Fact]
    public async Task SelectingAMonstersCorpse_IsLeftToTheRoom()
    {
        int wolf = await AWolfAsync();
        _world.Simulation.Combat.Combatants[wolf].Health = 0;
        await _world.TickAsync();

        _world.Zone.Removed.Should().BeEmpty("its body still lies there");
        (await _world.Simulation.SelectTargetAsync(new PlayerId(7), wolf, CancellationToken.None))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task SelectingSomethingElse_IsLeftToTheRoom()
    {
        await AWolfAsync();

        (await _world.Simulation.SelectTargetAsync(new PlayerId(7), 999, CancellationToken.None))
            .Should()
            .BeFalse();
    }
}

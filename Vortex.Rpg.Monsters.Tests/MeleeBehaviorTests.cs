using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Monsters.Behaviors;
using Vortex.Rpg.Monsters.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Monsters.Tests;

/// <summary>Spec M1a §5.3: rest, chase, fight, go home past the leash; never onto or after a safe
/// cell. A wolf is born at (1, 1), aggro radius 4, leash 8.</summary>
public sealed class MeleeBehaviorTests
{
    private readonly MonsterWorld _world = new();

    private int Wolf => _world.Zone.Spawned.Single().ObjectId;

    private Combatant WolfCombatant => _world.Simulation.Combat.Combatants[Wolf];

    private async Task WolfAndPlayerAtAsync(int x, int y, bool aggressive = true)
    {
        _world.Start(
            [MonsterWorld.WolvesAt(1, (1, 1)) with { Monster = "monster.test.wolf" }],
            content: MonsterWorld.Wolf with
            {
                Id = "monster.test.wolf",
                Aggressive = aggressive,
            }
        );
        await _world.EnterAsync(7, 1, x, y);
        await _world.TickAsync();
    }

    [Fact]
    public async Task AnAggressiveMonster_ChasesAPlayerInItsAggroRadius()
    {
        await WolfAndPlayerAtAsync(4, 1);

        WolfCombatant.Threat.Top.Should().Be(1);
        _world.Zone.Walks.Should().Contain((Wolf, 4, 1));
    }

    [Fact]
    public async Task APlayerOutOfItsAggroRadius_IsLeftAlone()
    {
        await WolfAndPlayerAtAsync(9, 1);

        WolfCombatant.Threat.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task APassiveMonster_IgnoresUntilHit()
    {
        await WolfAndPlayerAtAsync(2, 1, aggressive: false);
        WolfCombatant.Threat.IsEmpty.Should().BeTrue();

        await _world.Simulation.SelectTargetAsync(new PlayerId(7), Wolf, CancellationToken.None);
        await _world.TickAsync();
        await _world.TickAsync();

        WolfCombatant.TargetId.Should().Be(1, "hit, it fights back");
    }

    [Fact]
    public async Task APlayerOnASafeCell_IsNeverChased()
    {
        _world.Zone.SafeCells.Add((4, 1));

        await WolfAndPlayerAtAsync(4, 1);

        WolfCombatant.Threat.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task AtRange_TheMonsterAttacks()
    {
        await WolfAndPlayerAtAsync(2, 1);

        _world
            .Zone.Broadcasts.OfType<RpgCombatEventsMessageComposer>()
            .SelectMany(e => e.Hits)
            .Should()
            .Contain(h => h.AttackerId == Wolf && h.TargetId == 1);
    }

    [Fact]
    public async Task AMonsterPastItsLeash_GoesHomeInvulnerable_AndHeals()
    {
        await WolfAndPlayerAtAsync(4, 1);
        _world.Zone.Positions[Wolf] = (12, 1);
        WolfCombatant.Health = 10;

        await _world.TickAsync();

        WolfCombatant.Invulnerable.Should().BeTrue();
        WolfCombatant.Threat.IsEmpty.Should().BeTrue();
        _world.Zone.Walks.Last().Should().Be((Wolf, 1, 1));

        _world.Zone.Positions[Wolf] = (2, 1);
        _world.Zone.Walking.Remove(Wolf);
        _world.Zone.Positions[1] = (15, 15);
        await _world.TickAsync();

        WolfCombatant.Health.Should().Be(WolfCombatant.MaxHealth);
        WolfCombatant.Invulnerable.Should().BeFalse();
    }

    /// <summary>No path home: it is put back there and healed, not left invulnerable for good.</summary>
    [Fact]
    public async Task AMonsterThatCannotWalkHome_IsPutBackThere()
    {
        await WolfAndPlayerAtAsync(4, 1);
        _world.Zone.Positions[Wolf] = (12, 1);
        WolfCombatant.Health = 10;
        _world.Zone.PathsExist = false;

        await _world.TickAsync();

        _world.Zone.Teleports.Should().Contain((Wolf, 1, 1));
        WolfCombatant.Invulnerable.Should().BeFalse();
        WolfCombatant.Health.Should().Be(WolfCombatant.MaxHealth);
    }

    [Fact]
    public async Task ADeclaredBehavior_IsUsedByItsKey()
    {
        StatueBehavior statue = new();
        _world.Start(
            [MonsterWorld.WolvesAt(1, (1, 1)) with { Monster = "monster.test.statue" }],
            [new MeleeBehavior(), statue],
            MonsterWorld.Wolf with
            {
                Id = "monster.test.statue",
                Behavior = "statue",
            }
        );
        await _world.EnterAsync(7, 1, 2, 1);

        await _world.TickAsync();

        statue.Thoughts.Should().Be(1);
        WolfCombatant.Threat.IsEmpty.Should().BeTrue("a statue does nothing");
    }

    private sealed class StatueBehavior : IMonsterBehavior
    {
        public int Thoughts { get; private set; }

        public string Key => "statue";

        public void Think(MonsterBrain brain) => Thoughts++;
    }
}

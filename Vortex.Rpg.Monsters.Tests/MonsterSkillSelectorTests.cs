using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Systems;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Monsters.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Monsters.Tests;

/// <summary>
/// Spec M1c §6: in a fight, a monster tries its rules by priority and casts the first that passes;
/// a refused one (a cooldown) hands over to the next. A wolf at (1, 1) fights player 7 (avatar 1) at
/// (2, 1); the casting system is a recorder. Every random draw is 0.5: a 50 % chance never passes.
/// </summary>
public sealed class MonsterSkillSelectorTests
{
    private readonly MonsterWorld _world = new();
    private readonly RecordingCasting _casting = new();

    private static SkillDefinition Skill(string name, SkillTarget target, int range) =>
        new()
        {
            Id = $"skill.monster.{name}",
            Kind = "skill",
            Classes = [],
            Level = 1,
            Type = SkillType.Instant,
            Range = range,
            Target = target,
            Effects = [new SkillEffectDefinition { Kind = "damage", Base = 1 }],
        };

    private static readonly SkillDefinition Bite = Skill("bite", SkillTarget.Enemy, 1);
    private static readonly SkillDefinition Spit = Skill("spit", SkillTarget.Enemy, 4);
    private static readonly SkillDefinition Howl = Skill("howl", SkillTarget.Self, 0);
    private static readonly SkillDefinition Quake = Skill("quake", SkillTarget.Ground, 4);
    private static readonly SkillDefinition Mend = Skill("mend", SkillTarget.Ally, 4);
    private static readonly SkillDefinition Gone = Skill("gone", SkillTarget.Enemy, 4) with
    {
        Retired = true,
    };

    private static MonsterSkillRule Rule(
        SkillDefinition skill,
        int priority = 0,
        int minRange = 0,
        int below = 100,
        int chance = 100
    ) =>
        new()
        {
            Skill = skill.Id,
            Priority = priority,
            MinRange = minRange,
            BelowHealthPercent = below,
            Chance = chance,
        };

    private int WolfId => _world.Zone.Spawned[0].ObjectId;

    private Combatant WolfOf(int index) =>
        _world.Simulation.Combat.Combatants[_world.Zone.Spawned[index].ObjectId];

    private async Task FightAsync(int wolves = 1, params MonsterSkillRule[] rules)
    {
        MonsterDefinition wolf = MonsterWorld.Wolf with
        {
            Id = "monster.test.wolf",
            Skills = [.. rules],
        };
        (int, int)[] cells = wolves == 1 ? [(1, 1)] : [(1, 1), (1, 2)];
        _world.Start(
            [MonsterWorld.WolvesAt(wolves, cells) with { Monster = wolf.Id }],
            null,
            [_casting],
            wolf,
            Bite,
            Spit,
            Howl,
            Quake,
            Mend,
            Gone
        );
        await _world.EnterAsync(7, 1, 2, 1);
        await _world.TickAsync();
    }

    private string[] Asked => [.. _casting.Casts.Select(c => c.SkillId)];

    [Fact]
    public async Task TheHighestPriority_IsTriedFirst()
    {
        await FightAsync(1, Rule(Bite, priority: 1), Rule(Spit, priority: 5));

        Asked.Should().Equal(Spit.Id);
    }

    [Fact]
    public async Task OnATie_TheOrderOfTheFileWins()
    {
        await FightAsync(1, Rule(Bite), Rule(Spit));

        Asked.Should().Equal(Bite.Id);
    }

    [Fact]
    public async Task ARefusedRule_HandsOverToTheNext()
    {
        _casting.Refusals[Spit.Id] = "cooldown";

        await FightAsync(1, Rule(Spit, priority: 5), Rule(Bite, priority: 1));

        Asked.Should().Equal(Spit.Id, Bite.Id);
    }

    [Fact]
    public async Task ATargetTooClose_SkipsTheRule()
    {
        await FightAsync(1, Rule(Spit, priority: 5, minRange: 2), Rule(Bite));

        Asked.Should().Equal(Bite.Id);
    }

    [Fact]
    public async Task ARuleBelowHealth_WaitsForTheCasterToBeHurt()
    {
        await FightAsync(1, Rule(Bite, below: 50));
        Asked.Should().BeEmpty();

        WolfOf(0).Health = 10;
        await _world.TickAsync();

        Asked.Should().Equal(Bite.Id);
    }

    [Fact]
    public async Task ARuleWhoseChanceFails_IsSkipped()
    {
        await FightAsync(1, Rule(Spit, priority: 5, chance: 50), Rule(Bite, chance: 51));

        Asked.Should().Equal(Bite.Id);
    }

    [Fact]
    public async Task ARetiredSkill_IsSkipped()
    {
        await FightAsync(1, Rule(Gone, priority: 5), Rule(Bite));

        Asked.Should().Equal(Bite.Id);
    }

    [Fact]
    public async Task EachMode_AimsAsTheSpecSays()
    {
        _casting.Refusals[Bite.Id] = "cooldown";
        _casting.Refusals[Howl.Id] = "cooldown";
        _casting.Refusals[Quake.Id] = "cooldown";

        await FightAsync(1, Rule(Bite, 3), Rule(Howl, 2), Rule(Quake, 1));

        _casting
            .Casts.Select(c => (c.SkillId, c.TargetObjectId, c.X, c.Y))
            .Should()
            .Equal((Bite.Id, 1, -1, -1), (Howl.Id, WolfId, -1, -1), (Quake.Id, 0, 2, 1));
    }

    [Fact]
    public async Task AnAllySkill_PicksTheMostWoundedLiveMonster()
    {
        await FightAsync(2, Rule(Mend, below: 90));
        _casting.Casts.Clear();
        Combatant caster = WolfOf(0);
        Combatant other = WolfOf(1);

        caster.Health = 30;
        other.Health = 4;
        other.Invulnerable = true;
        await _world.TickAsync();

        _casting
            .Casts.Where(c => c.CasterObjectId == caster.ObjectId)
            .Select(c => c.TargetObjectId)
            .Should()
            .Equal(caster.ObjectId);

        _casting.Casts.Clear();
        other.Invulnerable = false;
        await _world.TickAsync();

        _casting
            .Casts.Where(c => c.CasterObjectId == caster.ObjectId)
            .Select(c => c.TargetObjectId)
            .Should()
            .Equal(other.ObjectId);
    }

    [Fact]
    public async Task NothingToCast_LeavesTheAttackAsItWas()
    {
        await FightAsync(1, Rule(Bite, below: 10));

        Asked.Should().BeEmpty();
        WolfOf(0).TargetId.Should().Be(1);
    }
}

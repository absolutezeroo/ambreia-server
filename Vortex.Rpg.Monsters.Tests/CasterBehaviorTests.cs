using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Monsters.Behaviors;
using Vortex.Rpg.Monsters.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Monsters.Tests;

/// <summary>
/// Spec M1c §4.3, plan decision 5: a caster stands at the least range of its offensive skills, else
/// at its attack's reach, and casts before it strikes. A shaman of reach 1 at (1, 1), player 7.
/// </summary>
public sealed class CasterBehaviorTests
{
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

    private static readonly SkillDefinition Bolt = Skill("bolt", SkillTarget.Enemy, 4);
    private static readonly SkillDefinition Spray = Skill("spray", SkillTarget.Ground, 6);
    private static readonly SkillDefinition Mend = Skill("mend", SkillTarget.Ally, 2);

    private readonly MonsterWorld _world = new();
    private readonly RecordingCasting _casting = new();

    private Task StartAsync(int playerX, params SkillDefinition[] skills) =>
        StartAsync(playerX, 100, skills);

    private async Task StartAsync(int playerX, int below, params SkillDefinition[] skills)
    {
        MonsterDefinition shaman = MonsterWorld.Wolf with
        {
            Id = "monster.test.shaman",
            Behavior = "caster",
            Skills =
            [
                .. skills.Select(s => new MonsterSkillRule
                {
                    Skill = s.Id,
                    BelowHealthPercent = below,
                }),
            ],
        };
        _world.Start(
            [MonsterWorld.WolvesAt(1, (1, 1)) with { Monster = shaman.Id, AggroRadius = 9 }],
            [new MeleeBehavior(), new CasterBehavior()],
            [_casting],
            shaman,
            Bolt,
            Spray,
            Mend
        );
        await _world.EnterAsync(7, 1, playerX, 1);
        await _world.TickAsync();
    }

    [Fact]
    public async Task ItStands_AtTheLeastRangeOfItsOffensiveSkills()
    {
        await StartAsync(9, Bolt, Spray, Mend);

        _world.Zone.WalkRanges.Last().Should().Be(4);
    }

    [Fact]
    public async Task ASkillItCannotUseYet_DoesNotHoldItBack()
    {
        await StartAsync(9, below: 50, Bolt);

        _world.Zone.WalkRanges.Last().Should().Be(1);
    }

    [Fact]
    public async Task WithoutAnOffensiveSkill_ItClosesToItsReach()
    {
        await StartAsync(9, Mend);

        _world.Zone.WalkRanges.Last().Should().Be(1);
    }

    [Fact]
    public async Task InRange_ItCastsWithoutClosingIn()
    {
        await StartAsync(4, Bolt);

        _casting.Casts.Select(c => c.SkillId).Should().Equal(Bolt.Id);
        _world.Zone.Walks.Should().BeEmpty();
    }
}

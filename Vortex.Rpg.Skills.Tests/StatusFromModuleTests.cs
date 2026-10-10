using System.Collections.Frozen;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Systems;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Skills.Tests.Support;
using Vortex.Rpg.Zones;
using Xunit;

namespace Vortex.Rpg.Skills.Tests;

/// <summary>
/// Spec M1c §7.1–§7.2: another module (the monsters' phases) puts a status that lasts until taken off,
/// and takes it off; a rank's immunities turn a status away with <c>immune</c>, while an interrupt
/// still breaks a cast. Mage 7 (avatar 1) at (5, 5).
/// </summary>
public sealed class StatusFromModuleTests
{
    private static readonly StatusDefinition Enraged = new()
    {
        Id = "status.enraged",
        Kind = "status",
        Type = StatusType.Buff,
        DurationMs = 1000,
        Modifiers =
        [
            new StatModifierDefinition { Stat = ModifierStat.DamageDealtPercent, Flat = 30 },
        ],
    };

    private static readonly SkillDefinition Bash = CastTests.Skill("skill.mage.bash") with
    {
        Effects =
        [
            new SkillEffectDefinition { Kind = "apply_status", Status = CastTests.Stunned.Id },
            new SkillEffectDefinition { Kind = "interrupt" },
        ],
    };

    private static readonly SkillDefinition Spit = CastTests.Skill(
        "skill.monster.spit",
        SkillType.Cast,
        range: 4,
        cost: 0,
        castMs: 2000
    ) with
    {
        Classes = [],
    };

    private readonly SkillWorld _world = SkillWorld.WithCasting(
        Enraged,
        Bash,
        Spit,
        CastTests.Stunned
    );

    private Combatant Boss()
    {
        MonsterDefinition d = SkillWorld.WolfDefinition;
        int id = _world.Zone.SpawnMonster(new MonsterAppearance(d.Id, d.Figure, 1), 6, 5);

        return _world.Combat.AddMonster(
            id,
            1,
            d,
            new RankScale(1, 1, 1, FrozenSet.ToFrozenSet([StatusType.Stun, StatusType.Slow]))
        );
    }

    [Fact]
    public async Task APermanentStatus_NeverEnds_AndIsSentWithoutAnEnd()
    {
        await _world.PlayerAsync();
        Combatant boss = Boss();

        _world.Combat.ApplyStatus(boss, boss, Enraged.Id, permanent: true);
        await _world.RunAsync(600000);

        boss.DamageDealtPercent.Should().Be(30);
        _world
            .Broadcast<RpgStatusesMessageComposer>()
            .SelectMany(s => s.Combatants)
            .Last(c => c.ObjectId == boss.ObjectId)
            .Statuses.Should()
            .ContainSingle(s => s.StatusId == Enraged.Id && s.RemainingMs == -1);
    }

    [Fact]
    public async Task RemoveStatus_TakesItOff()
    {
        await _world.PlayerAsync();
        Combatant boss = Boss();
        _world.Combat.ApplyStatus(boss, boss, Enraged.Id, permanent: true);
        await _world.TickAsync();

        _world.Combat.RemoveStatus(boss, Enraged.Id);
        await _world.TickAsync();

        boss.DamageDealtPercent.Should().Be(0);
    }

    [Fact]
    public async Task AStatusTheRankIgnores_IsImmune()
    {
        await _world.PlayerAsync();
        Combatant boss = Boss();

        _world.Cast(Bash.Id, boss.ObjectId).Should().BeNull();
        await _world.TickAsync();

        boss.Stunned.Should().BeFalse();
        _world
            .Broadcast<RpgSkillEventsMessageComposer>()
            .SelectMany(e => e.Outcomes)
            .Should()
            .Contain(o => o.TargetObjectId == boss.ObjectId && o.Outcome == RpgSkillOutcome.Immune);
    }

    [Fact]
    public async Task AnInterrupt_StillBreaksAnImmuneBossCast()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant boss = Boss();
        _world.Combat.Cast(new CastRequest(boss.ObjectId, Spit.Id, me.ObjectId, -1, -1));
        await _world.TickAsync();

        _world.Cast(Bash.Id, boss.ObjectId);
        await _world.TickAsync();

        _world
            .Broadcast<RpgCastEndMessageComposer>()
            .SelectMany(e => e.Ends)
            .Should()
            .ContainSingle(e =>
                e.CasterObjectId == boss.ObjectId && e.Reason == RpgCastEnd.Interrupted
            );
    }
}

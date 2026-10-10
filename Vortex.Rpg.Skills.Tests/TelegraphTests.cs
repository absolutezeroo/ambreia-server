using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Systems;
using Vortex.Rpg.Combat.Telegraphs;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Skills.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Skills.Tests;

/// <summary>
/// Spec M1c §8: a telegraphed cast fixes its cells when it starts, shows them to the zone, and lands
/// on whoever still stands there. A wolf at (7, 5) casts a cone (3, 90°, 1.5 s) west at mage 7 (avatar
/// 1) at (5, 5).
/// </summary>
public sealed class TelegraphTests
{
    private static readonly SkillDefinition Sweep = CastTests.Skill(
        "skill.monster.sweep",
        SkillType.Cast,
        range: 3,
        cost: 0,
        castMs: 1500
    ) with
    {
        Classes = [],
        Telegraph = new SkillTelegraph
        {
            Shape = TelegraphShape.Cone,
            Size = 3,
            Angle = 90,
        },
    };

    private static readonly SkillDefinition Single = Sweep with
    {
        Id = "skill.monster.single",
        MaxTargets = 1,
    };

    private readonly SkillWorld _world = SkillWorld.WithCasting(Sweep, Single);

    private string? Telegraph(Combatant caster, Combatant target, SkillDefinition? skill = null) =>
        _world.Combat.Cast(
            new CastRequest(caster.ObjectId, (skill ?? Sweep).Id, target.ObjectId, -1, -1)
        );

    private RpgSkillOutcome[] Outcomes() =>
        [.. _world.Broadcast<RpgSkillEventsMessageComposer>().SelectMany(e => e.Outcomes)];

    [Fact]
    public async Task TheCells_AreShownOnce_WhenTheCastStarts()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(x: 7, y: 5);

        Telegraph(wolf, me).Should().BeNull();
        await _world.RunAsync(1000);

        RpgTelegraph shown = _world
            .Broadcast<RpgTelegraphsMessageComposer>()
            .Should()
            .ContainSingle()
            .Which.Telegraphs.Should()
            .ContainSingle()
            .Subject;
        (shown.CasterObjectId, shown.SkillId, shown.Shape, shown.DelayMs)
            .Should()
            .Be((wolf.ObjectId, Sweep.Id, "cone", 1500));
        shown
            .Cells.Should()
            .Equal(TelegraphCells.Of(Sweep.Telegraph!, (7, 5), (5, 5), (_, _) => true));
    }

    [Fact]
    public async Task StepingOutBeforeTheImpact_Dodges()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(x: 7, y: 5);
        Telegraph(wolf, me);
        await _world.TickAsync();

        _world.Zone.Positions[me.ObjectId] = (5, 9);
        await _world.RunAsync(1500);

        Outcomes().Should().NotContain(o => o.TargetObjectId == me.ObjectId);
        _world
            .Broadcast<RpgCastEndMessageComposer>()
            .SelectMany(e => e.Ends)
            .Should()
            .ContainSingle(e => e.Reason == RpgCastEnd.Done);
    }

    [Fact]
    public async Task SteppingInBeforeTheImpact_IsHit()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant other = await _world.PlayerAsync(player: 8, objectId: 2, x: 5, y: 9);
        Combatant wolf = _world.Wolf(x: 7, y: 5);
        Telegraph(wolf, me);
        await _world.TickAsync();

        _world.Zone.Positions[other.ObjectId] = (6, 5);
        await _world.RunAsync(1500);

        Outcomes().Should().Contain(o => o.TargetObjectId == other.ObjectId);
    }

    [Fact]
    public async Task APlayerOnASafeCell_IsNotHitByATelegraph()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(x: 7, y: 5);
        Telegraph(wolf, me);
        await _world.TickAsync();

        _world.Zone.SafeCells.Add((5, 5));
        await _world.RunAsync(1500);

        Outcomes().Should().NotContain(o => o.TargetObjectId == me.ObjectId);
    }

    [Fact]
    public async Task OneTargetMost_IsTheNearestToTheOrigin()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant near = await _world.PlayerAsync(player: 8, objectId: 2, x: 6, y: 5);
        Combatant wolf = _world.Wolf(x: 7, y: 5);
        Telegraph(wolf, me, Single).Should().BeNull();
        await _world.RunAsync(1750);

        Outcomes().Select(o => o.TargetObjectId).Should().Equal(near.ObjectId);
    }

    [Fact]
    public async Task TheFirstTargetLeaving_DoesNotStopATelegraph()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant other = await _world.PlayerAsync(player: 8, objectId: 2, x: 6, y: 5);
        Combatant wolf = _world.Wolf(x: 7, y: 5);
        Telegraph(wolf, me);
        await _world.TickAsync();

        await _world.Combat.PlayerLeftAsync(new PlayerId(7), CancellationToken.None);
        await _world.RunAsync(1500);

        Outcomes().Should().Contain(o => o.TargetObjectId == other.ObjectId);
    }

    [Fact]
    public async Task ACasterDyingMidTelegraph_LandsNothing()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(x: 7, y: 5);
        Telegraph(wolf, me);
        await _world.TickAsync();

        _world.Combat.Damage(me, wolf, 1000, _world.Now);
        await _world.RunAsync(1500);

        Outcomes().Should().NotContain(o => o.TargetObjectId == me.ObjectId);
        _world
            .Broadcast<RpgCastEndMessageComposer>()
            .SelectMany(e => e.Ends)
            .Should()
            .ContainSingle(e => e.Reason == RpgCastEnd.Cancelled);
    }

    [Fact]
    public async Task APlayerComingIn_SeesTheTelegraphWithItsTimeLeft()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(x: 7, y: 5);
        Telegraph(wolf, me);
        await _world.TickAsync();

        _world
            .Combat.EntryVitals(new PlayerId(7))
            .OfType<RpgTelegraphsMessageComposer>()
            .Should()
            .ContainSingle()
            .Which.Telegraphs.Should()
            .ContainSingle(t => t.CasterObjectId == wolf.ObjectId && t.DelayMs == 1250);
    }

    [Fact]
    public async Task TheFirstOneIn_HearsNothingOfTheEmptyZonesPast()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(x: 7, y: 5);
        Telegraph(wolf, me);
        await _world.Combat.PlayerLeftAsync(new PlayerId(7), CancellationToken.None);

        await _world.PlayerAsync(player: 8, objectId: 2, x: 1, y: 1);
        await _world.TickAsync();

        _world.Broadcast<RpgTelegraphsMessageComposer>().Should().BeEmpty();
    }
}

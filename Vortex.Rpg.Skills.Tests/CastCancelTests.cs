using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Skills.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Skills.Tests;

/// <summary>Spec M1b §6.4: what stops a cast or a channel, what it costs then, and that nothing lands
/// after. Mage 7 (avatar 1, mana 100) at (5, 5), a tough wolf at (6, 5).</summary>
public sealed class CastCancelTests
{
    private static readonly SkillDefinition Bolt = CastTests.Bolt;
    private static readonly SkillDefinition Rain = CastTests.Rain;
    private static readonly SkillDefinition Stride = CastTests.Skill(
        "skill.mage.stride",
        SkillType.Cast,
        cost: 10,
        castMs: 2000,
        moveAllowed: true
    );

    private readonly SkillWorld _world = SkillWorld.WithCasting(
        Bolt,
        Rain,
        Stride,
        CastTests.Stunned
    );

    private Combatant ToughWolf() =>
        _world.Wolf(
            definition: SkillWorld.WolfDefinition with
            {
                Base = SkillWorld.WolfDefinition.Base with { Health = 500 },
            }
        );

    private IEnumerable<string> Ends() =>
        _world.Broadcast<RpgCastEndMessageComposer>().SelectMany(e => e.Ends).Select(e => e.Reason);

    [Fact]
    public async Task Walking_Cancels_UnlessTheSkillAllowsIt()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        _world.Cast(Bolt.Id, wolf.ObjectId);

        _world.Combat.PlayerWalked(new PlayerId(7));
        await _world.TickAsync();

        (Ends().Single(), me.Busy).Should().Be((RpgCastEnd.Cancelled, false));

        await _world.RunAsync(1000);
        _world.Cast(Stride.Id, wolf.ObjectId);
        _world.Combat.PlayerWalked(new PlayerId(7));
        await _world.RunAsync(2000);

        Ends().Last().Should().Be(RpgCastEnd.Done);
    }

    [Fact]
    public async Task TheCancelPacket_Cancels_AndCostsNothing()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        _world.Cast(Bolt.Id, wolf.ObjectId);

        _world.Combat.CancelCast(new PlayerId(7));
        await _world.RunAsync(2500);

        Ends().Should().Equal(RpgCastEnd.Cancelled);
        (wolf.Health, me.Mana).Should().Be((500, 100));
    }

    /// <summary>Review Focus 3: a stun breaks the cast.</summary>
    [Fact]
    public async Task AStunMidCast_Interrupts()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        _world.Cast(Bolt.Id, wolf.ObjectId);
        await _world.TickAsync();

        _world.Statuses.Apply(_world.Combat, wolf, me, CastTests.Stunned, _world.Now);
        await _world.RunAsync(2000);

        Ends().Should().Equal(RpgCastEnd.Interrupted);
        wolf.Health.Should().Be(500);
    }

    /// <summary>A stun breaks the cast the moment it lands, not at the caster's next turn: a cast
    /// ending later in the same tick does not resolve stunned.</summary>
    [Fact]
    public async Task AStun_InterruptsAtOnce()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        _world.Cast(Bolt.Id, wolf.ObjectId);

        _world.Statuses.Apply(_world.Combat, wolf, me, CastTests.Stunned, _world.Now);

        me.Busy.Should().BeFalse();
    }

    [Fact]
    public async Task AnInterruptEffect_Interrupts()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        _world.Cast(Bolt.Id, wolf.ObjectId);

        _world.Combat.Interrupt(me);
        await _world.TickAsync();

        Ends().Should().Equal(RpgCastEnd.Interrupted);
    }

    /// <summary>Review Focus 2: the target killed by someone else before the cast lands.</summary>
    [Fact]
    public async Task TheTargetDyingMidCast_CancelsTheCast()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        _world.Cast(Bolt.Id, wolf.ObjectId);
        await _world.TickAsync();

        wolf.Health = 0;
        await _world.RunAsync(2000);

        Ends().Should().Equal(RpgCastEnd.Cancelled);
        (me.Mana, me.Busy).Should().Be((100, false));
    }

    /// <summary>Review Focus 1: leaving mid-cast — nothing lands, nothing is paid, the zone sees it
    /// end.</summary>
    [Fact]
    public async Task LeavingMidCast_CancelsWithoutCost_AndLandsNothing()
    {
        await _world.PlayerAsync();
        await _world.PlayerAsync(player: 8, objectId: 2, x: 4, y: 5);
        Combatant wolf = ToughWolf();
        _world.Cast(Bolt.Id, wolf.ObjectId);
        await _world.TickAsync();

        await _world.Combat.PlayerLeftAsync(new PlayerId(7), CancellationToken.None);
        await _world.RunAsync(3000);

        wolf.Health.Should().Be(500);
        _world.Calls.Should().Contain(c => c.StartsWith("vitals 7: 100/100"));
        Ends().Should().Equal(RpgCastEnd.Cancelled);
    }

    [Fact]
    public async Task DyingMidChannel_KeepsWhatLanded()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        _world.Cast(Rain.Id, 0, 6, 5);
        await _world.RunAsync(1000);
        wolf.Health.Should().Be(490);

        me.Health = 0;
        await _world.RunAsync(2000);

        wolf.Health.Should().Be(490);
        Ends().Should().Equal(RpgCastEnd.Cancelled);
    }

    [Fact]
    public async Task AWalk_DropsAPendingApproach_Silently()
    {
        await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(12, 5);
        _world.Cast(Bolt.Id, wolf.ObjectId);

        _world.Combat.PlayerWalked(new PlayerId(7));
        _world.Zone.Positions[1] = (8, 5);
        await _world.RunAsync(500);

        _world.Broadcast<RpgCastStartMessageComposer>().Should().BeEmpty();
        _world.SentTo<RpgSkillResultMessageComposer>(7).Should().BeEmpty();
    }
}

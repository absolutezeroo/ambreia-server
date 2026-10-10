using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;
using static Vortex.Rooms.Tests.Zones.ZoneVisits;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// Spec Z1 §5.1: what the simulation sees happen during its tick (a monster killed) is held, then
/// runs the zone's triggers once the tick is over — for players still in the room.
/// </summary>
public sealed class ZoneRaisedTriggersTests
{
    private static ZoneTrigger OnKill(string target, string says) =>
        new()
        {
            Id = "t_" + target,
            Type = ZoneTriggerType.MonsterKilled,
            Target = target,
            Actions = [new ShowDialogueAction(says)],
        };

    private static async Task<RoomHarness> InsideAsync()
    {
        RoomHarness harness = await ZoneAsync(
            ZoneSamples.Minimal() with
            {
                Triggers = [OnKill("wolves", "point"), OnKill("monster.forest.wolf", "espèce")],
            }
        );
        await EnterAsync(harness);

        return harness;
    }

    private static string[] Bubbles(RoomHarness harness) =>
        [
            .. Sent<NotificationDialogMessageComposer>(harness)
                .Select(n => n.Parameters.Single(p => p.Key == "message").Value),
        ];

    [Fact]
    public async Task AKill_RunsTheTriggersOnItsSpawnAndItsKind_AfterTheTick()
    {
        RoomHarness harness = await InsideAsync();

        harness.Grain.CombatRuntime.RaiseTrigger(Walker, ZoneTriggerType.MonsterKilled, "wolves");
        harness.Grain.CombatRuntime.RaiseTrigger(
            Walker,
            ZoneTriggerType.MonsterKilled,
            "monster.forest.wolf"
        );
        Bubbles(harness).Should().BeEmpty("nothing runs inside the simulation's tick");

        await harness.Grain.CombatRuntime.FlushAsync();

        Bubbles(harness).Should().Equal("point", "espèce");
    }

    [Fact]
    public async Task APlayerWhoLeft_GetsNothing()
    {
        RoomHarness harness = await InsideAsync();

        harness.Grain.CombatRuntime.RaiseTrigger(
            new PlayerId(99),
            ZoneTriggerType.MonsterKilled,
            "wolves"
        );
        await harness.Grain.CombatRuntime.FlushAsync();

        Bubbles(harness).Should().BeEmpty();
    }

    [Fact]
    public async Task ASignal_RunsOnce()
    {
        RoomHarness harness = await InsideAsync();
        harness.Grain.CombatRuntime.RaiseTrigger(Walker, ZoneTriggerType.MonsterKilled, "wolves");

        await harness.Grain.CombatRuntime.FlushAsync();
        await harness.Grain.CombatRuntime.FlushAsync();

        Bubbles(harness).Should().Equal("point");
    }
}

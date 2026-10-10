using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Rooms.Grains.Zones.Runtime;
using Vortex.Rooms.Grains.Zones.Triggers;
using Vortex.Rooms.Tests.Support;
using Vortex.Rpg.Zones.Rules;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;
using static Vortex.Rooms.Tests.Zones.ZoneVisits;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// Conditions and actions a module declares (spec Z1 §4.2): the room asks them beside its own, a
/// rule nobody declared is false or skipped, and a rule declared twice is the last one's.
/// </summary>
public sealed class ZoneRuleRegistryTests
{
    [Fact]
    public async Task AConditionDeclaredByAModule_IsAnswered()
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(
            ZoneSamples.Minimal(),
            conditions: [new AlwaysTrue<PlayerLevelCondition>()]
        );

        harness
            .Grain.ZoneTriggers.ConditionsHold([new PlayerLevelCondition(1, 0)], Walker)
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task AConditionNobodyDeclared_IsFalse()
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(ZoneSamples.Minimal());

        harness
            .Grain.ZoneTriggers.ConditionsHold([new HasItemCondition("item.x", 1)], Walker)
            .Should()
            .BeFalse();
    }

    private static ZoneTriggerContext Context(RoomHarness harness) =>
        new(
            harness.Grain,
            harness.Grain._state.ZoneRuntime!,
            Walker,
            null,
            DateTimeOffset.Now,
            ZoneRuntimeState.NoFacts
        );

    [Fact]
    public async Task ARuleDeclaredTwice_TheLastWins()
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(ZoneSamples.Minimal());
        ZoneRuleRegistry registry = new(
            [new AlwaysFalse<PlayerLevelCondition>(), new AlwaysTrue<PlayerLevelCondition>()],
            [],
            [],
            NullLogger<ZoneRuleRegistry>.Instance
        );

        registry.AllHold([new PlayerLevelCondition(1, 0)], Context(harness)).Should().BeTrue();
    }

    /// <summary>A step asks this: a broken piece shuts its content, it does not stop the walk.</summary>
    [Fact]
    public async Task AConditionThatThrows_IsFalse()
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(ZoneSamples.Minimal());
        ZoneRuleRegistry registry = new(
            [new Throwing<PlayerLevelCondition>()],
            [],
            [],
            NullLogger<ZoneRuleRegistry>.Instance
        );

        registry.AllHold([new PlayerLevelCondition(1, 0)], Context(harness)).Should().BeFalse();
    }

    /// <summary>
    /// Declared the way a module does: the piece answers through the container, and declaring it
    /// twice declares it once — one declaration for the validator, no "declared twice" at start.
    /// </summary>
    [Fact]
    public void ARuleDeclaredThroughTheContainer_AnswersAndIsDeclaredOnce()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddZoneCondition<AlwaysTrue<PlayerLevelCondition>>();
        services.AddZoneCondition<AlwaysTrue<PlayerLevelCondition>>();
        services.AddSingleton<ZoneRuleRegistry>();
        using ServiceProvider provider = services.BuildServiceProvider();

        provider
            .GetRequiredService<ZoneRuleRegistry>()
            .Answers(typeof(PlayerLevelCondition))
            .Should()
            .BeTrue();
        provider
            .GetServices<IZoneRuleDeclaration>()
            .Count(d => d.RuleType == typeof(PlayerLevelCondition))
            .Should()
            .Be(1);
    }

    /// <summary>A clock sets a trigger off for nobody: a module's action sees PlayerId.Invalid.</summary>
    [Fact]
    public async Task AClockTrigger_ReachesAModuleAsNobody()
    {
        RecordingAction<SpawnGroupAction> spawn = new();
        ZoneDocument doc = ZoneSamples.Minimal() with
        {
            Triggers =
            [
                new ZoneTrigger
                {
                    Id = "t_clock",
                    Type = ZoneTriggerType.Timer,
                    Target = "60",
                    Actions = [new SpawnGroupAction("ambush")],
                },
            ],
        };
        RoomHarness harness = await RoomHarness.CreateZoneAsync(doc, actions: [spawn]);
        ManualClock clock = new(DateTimeOffset.UnixEpoch);
        harness.Grain.ZoneTriggers.Time = clock;

        await harness.Grain.ZoneTriggers.TickAsync(default);
        clock.Advance(TimeSpan.FromSeconds(61));
        await harness.Grain.ZoneTriggers.TickAsync(default);

        spawn.Runs.Should().ContainSingle().Which.Ctx.PlayerId.Should().Be(PlayerId.Invalid);
    }

    [Fact]
    public async Task AnActionDeclaredByAModule_RunsForThePlayer()
    {
        RecordingAction<SpawnGroupAction> spawn = new();
        ZoneDocument doc = ZoneSamples.Minimal() with
        {
            Zones = [Area("market", "Town", string.Empty, 3, 5)],
            Triggers =
            [
                new ZoneTrigger
                {
                    Id = "t_market",
                    Type = ZoneTriggerType.EnterZone,
                    Target = "market",
                    Actions = [new SpawnGroupAction("ambush")],
                },
            ],
        };
        RoomHarness harness = await RoomHarness.CreateZoneAsync(doc, actions: [spawn]);
        harness.LogicFactory = () => FakeProxy.Create<IRoomPlayerLogic>(_ => null);
        await EnterAsync(harness);

        await WalkAsync(harness, 4, 2);

        spawn.Runs.Should().ContainSingle();
        spawn.Runs[0].Action.Spawn.Should().Be("ambush");
        spawn.Runs[0].Ctx.PlayerId.Should().Be(Walker);
        spawn.Runs[0].Ctx.Simulation.Should().BeSameAs(harness.Grain.Simulation);
    }
}

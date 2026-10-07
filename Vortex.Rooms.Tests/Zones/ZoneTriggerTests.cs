using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rooms.Events.Zone;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;
using static Vortex.Rooms.Tests.Zones.ZoneVisits;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// Triggers at run time (plan D11–D15): an area entry, a furni use or a clock fires the trigger
/// whose target matches, its conditions all hold, its actions run in order; a per-player cooldown,
/// a global one for the clock, and a chain of triggers stops at depth 8.
/// </summary>
public sealed class ZoneTriggerTests
{
    private static ZoneTrigger OnEnter(string area, params ZoneAction[] actions) =>
        new()
        {
            Id = "t_" + area,
            Type = ZoneTriggerType.EnterZone,
            Target = area,
            Actions = [.. actions],
        };

    private static ZoneDocument Market(params ZoneTrigger[] triggers) =>
        ZoneSamples.Minimal() with
        {
            Zones = [Area("market", "Town", string.Empty, 3, 5)],
            Triggers = [.. triggers],
        };

    private static string[] Bubbles(RoomHarness harness) =>
        [
            .. Sent<NotificationDialogMessageComposer>(harness)
                .Select(n => n.Parameters.Single(p => p.Key == "message").Value),
        ];

    [Fact]
    public async Task EnteringTheTargetArea_RunsTheActions()
    {
        RoomHarness harness = await ZoneAsync(
            Market(OnEnter("market", new ShowDialogueAction("Bienvenue au marché")))
        );
        await EnterAsync(harness);

        await WalkAsync(harness, 4, 2);

        Bubbles(harness).Should().Equal("Bienvenue au marché");
    }

    [Fact]
    public async Task AnInactiveTrigger_NeverFires()
    {
        RoomHarness harness = await ZoneAsync(
            Market(OnEnter("market", new ShowDialogueAction("x")) with { Active = false })
        );
        await EnterAsync(harness);

        await WalkAsync(harness, 4, 2);

        Bubbles(harness).Should().BeEmpty();
    }

    [Fact]
    public async Task AFailedCondition_StopsTheTrigger()
    {
        RoomHarness harness = await ZoneAsync(
            Market(
                OnEnter("market", new ShowDialogueAction("x")) with
                {
                    Conditions = [new PlayerLevelCondition(10, 99)],
                }
            )
        );
        await EnterAsync(harness);

        await WalkAsync(harness, 4, 2);

        Bubbles(harness).Should().BeEmpty();
    }

    [Fact]
    public async Task TimeOfDay_ReadsTheServerClock_AcrossMidnight()
    {
        ZoneTrigger night = OnEnter("market", new ShowDialogueAction("nuit")) with
        {
            Conditions = [new TimeOfDayCondition(22, 6)],
        };
        RoomHarness harness = await ZoneAsync(Market(night));
        harness.Grain.ZoneTriggers.Time = new ManualClock(
            new DateTimeOffset(2026, 10, 7, 23, 30, 0, TimeSpan.Zero)
        );
        await EnterAsync(harness);

        await WalkAsync(harness, 4, 2);

        Bubbles(harness).Should().Equal("nuit");
    }

    [Fact]
    public async Task ThePlayerCooldown_HoldsTheSecondEntry()
    {
        RoomHarness harness = await ZoneAsync(
            Market(OnEnter("market", new ShowDialogueAction("x")) with { CooldownMs = 600000 })
        );
        await EnterAsync(harness);

        await WalkAsync(harness, 4, 2);
        await WalkAsync(harness, 1, 2);
        await WalkAsync(harness, 4, 2);

        Bubbles(harness).Should().ContainSingle();
    }

    [Fact]
    public async Task DeactivatingAnArea_SilencesIt()
    {
        RoomHarness harness = await ZoneAsync(
            Market(
                OnEnter("market", new ShowDialogueAction("x")),
                new ZoneTrigger
                {
                    Id = "close",
                    Type = ZoneTriggerType.EnterZone,
                    Target = "porch",
                    Actions = [new DeactivateZoneAction("market")],
                }
            ) with
            {
                Zones =
                [
                    Area("market", "Town", string.Empty, 3, 5),
                    Area("porch", "Town", string.Empty, 1, 1),
                ],
            }
        );
        await EnterAsync(harness);

        await WalkAsync(harness, 1, 2);
        await WalkAsync(harness, 4, 2);

        Bubbles(harness).Should().BeEmpty();
        harness
            .RoomEvents.OfType<ZoneAreaEnteredEvent>()
            .Select(e => e.AreaId)
            .Should()
            .Equal("porch");
    }

    [Fact]
    public async Task OpenDoor_OpensTheNamedDoor()
    {
        RoomHarness harness = await ZoneWithDecorAsync(
            Market(OnEnter("market", new OpenDoorAction("gate"))) with
            {
                Decor =
                [
                    new ZoneDecorObject
                    {
                        Id = "gate",
                        Definition = ZoneSamples.Throne,
                        Class = "throne",
                        X = 0,
                        Y = 0,
                        Interaction = "door",
                    },
                ],
            }
        );
        await EnterAsync(harness);

        await WalkAsync(harness, 4, 2);

        harness.Grain._state.ZoneRuntime!.Doors[1].Should().BeTrue();
    }

    [Fact]
    public async Task Teleport_MovesThePlayerToTheEntry()
    {
        RoomHarness harness = await ZoneAsync(
            Market(OnEnter("market", new TeleportAction("village", "tower"))) with
            {
                Entries = [Entry("tower", ZoneEntryKinds.Teleport, 5, 5)],
            }
        );
        await EnterAsync(harness);

        await WalkAsync(harness, 4, 2);

        (AvatarOf(harness).X, AvatarOf(harness).Y).Should().Be((5, 5));
    }

    [Fact]
    public async Task ALoopOfTriggers_StopsAtDepthEight()
    {
        // Entering "a" teleports into "b", entering "b" teleports back into "a": forever, but for
        // the depth limit.
        RoomHarness harness = await ZoneAsync(
            ZoneSamples.Minimal() with
            {
                Zones =
                [
                    Area("a", "Town", string.Empty, 2, 2),
                    Area("b", "Town", string.Empty, 4, 4),
                ],
                Entries =
                [
                    Entry("in_a", ZoneEntryKinds.Teleport, 2, 4),
                    Entry("in_b", ZoneEntryKinds.Teleport, 4, 4),
                ],
                Triggers =
                [
                    OnEnter("a", new TeleportAction("village", "in_b")),
                    OnEnter("b", new TeleportAction("village", "in_a")),
                ],
            }
        );
        await EnterAsync(harness);

        await WalkAsync(harness, 2, 2);

        harness.RoomEvents.OfType<ZoneAreaEnteredEvent>().Count().Should().BeInRange(8, 10);
    }

    [Fact]
    public async Task ATimer_FiresEachPeriod_ForEveryone()
    {
        RoomHarness harness = await ZoneAsync(
            ZoneSamples.Minimal() with
            {
                Triggers =
                [
                    new ZoneTrigger
                    {
                        Id = "bell",
                        Type = ZoneTriggerType.Timer,
                        Target = "60",
                        Actions = [new ShowDialogueAction("La cloche sonne")],
                    },
                ],
            }
        );
        ManualClock clock = new(DateTimeOffset.UnixEpoch);
        harness.Grain.ZoneTriggers.Time = clock;
        await EnterAsync(harness);

        await harness.Grain.ZoneTriggers.TickAsync(default);
        clock.Advance(TimeSpan.FromSeconds(61));
        await harness.Grain.ZoneTriggers.TickAsync(default);
        clock.Advance(TimeSpan.FromSeconds(10));
        await harness.Grain.ZoneTriggers.TickAsync(default);

        Bubbles(harness).Should().Equal("La cloche sonne");
    }

    [Fact]
    public async Task UsingAFurni_FiresItsInteractTrigger()
    {
        RoomHarness harness = await ZoneAsync(
            ZoneSamples.Minimal() with
            {
                Triggers =
                [
                    new ZoneTrigger
                    {
                        Id = "pull",
                        Type = ZoneTriggerType.Interact,
                        Target = "lever",
                        Actions = [new ShowDialogueAction("Un grondement")],
                    },
                ],
            }
        );
        await EnterAsync(harness);

        await harness.Grain.ZoneTriggers.OnRoomEventAsync(
            new ZoneDecorUsedEvent
            {
                RoomId = harness.Grain.RoomId,
                PlayerId = Walker,
                DecorId = "lever",
            },
            default
        );

        Bubbles(harness).Should().Equal("Un grondement");
    }
}

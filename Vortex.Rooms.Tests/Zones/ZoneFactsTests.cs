using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Rooms.Tests.Support;
using Vortex.Rpg.Characters.Zones;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;
using static Vortex.Rooms.Tests.Zones.ZoneVisits;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// Spec Z1 §4.3: the zone keeps what it knows of each player, part by part — read on entry before
/// the arrival step, replaced by a newer part when its module pushes one, forgotten on exit.
/// </summary>
public sealed class ZoneFactsTests
{
    private static long _sequence;

    /// <summary>The character's part, each one newer than the last.</summary>
    private static CharacterZoneFacts Level(int level, params (string Item, int Count)[] items)
    {
        ImmutableDictionary<string, int>.Builder owned = ImmutableDictionary.CreateBuilder<
            string,
            int
        >();

        foreach ((string item, int count) in items)
        {
            owned[item] = count;
        }

        return new CharacterZoneFacts
        {
            Sequence = Interlocked.Increment(ref _sequence),
            Level = level,
            Items = owned.ToImmutable(),
        };
    }

    private static CharacterZoneFacts Held(RoomHarness harness) =>
        (CharacterZoneFacts)
            harness.Grain._state.ZoneRuntime!.Facts[Walker][typeof(CharacterZoneFacts)];

    /// <summary>Spec Z1 §4.4, plugged in: the characters module's condition reads the zone's facts.</summary>
    [Fact]
    public async Task ARestrictedArea_LetsInOnlyTheLevelItAsks()
    {
        ZoneDocument doc = ZoneSamples.Minimal() with
        {
            Zones =
            [
                Area("vault", ZoneAreaKinds.Restricted, "Chambre forte", 3, 5) with
                {
                    Conditions = [new PlayerLevelCondition(2, 0)],
                },
            ],
        };
        RoomHarness harness = await RoomHarness.CreateZoneAsync(
            doc,
            conditions: [new PlayerLevelEvaluator()]
        );
        harness.LogicFactory = () => FakeProxy.Create<IRoomPlayerLogic>(_ => null);
        harness.Facts[Walker] = Level(1);
        await EnterAsync(harness);

        await WalkAsync(harness, 4, 2);
        AvatarOf(harness).X.Should().BeLessThan(3, "a level 1 is kept out");

        await harness.Grain.UpdatePlayerFactsAsync(Walker, Level(2));
        await WalkAsync(harness, 4, 2);

        (AvatarOf(harness).X, AvatarOf(harness).Y).Should().Be((4, 2));
    }

    /// <summary>
    /// The arrival step runs inside the attach and sets off the EnterZone triggers of the arrival
    /// area: the facts are already held then, or a level-10 player would fail a "level ≥ 5".
    /// </summary>
    [Fact]
    public async Task TheArrivalStep_AlreadySeesTheFacts()
    {
        ZoneDocument doc = ZoneSamples.Minimal() with
        {
            Zones = [Area("gate", "Custom", string.Empty, 0, 1)],
            Triggers =
            [
                new ZoneTrigger
                {
                    Id = "t_gate",
                    Type = ZoneTriggerType.EnterZone,
                    Target = "gate",
                    Conditions = [new PlayerLevelCondition(5, 0)],
                    Actions = [new ShowDialogueAction("bienvenue")],
                },
            ],
        };
        RoomHarness harness = await RoomHarness.CreateZoneAsync(
            doc,
            conditions: [new PlayerLevelEvaluator()]
        );
        harness.LogicFactory = () => FakeProxy.Create<IRoomPlayerLogic>(_ => null);
        harness.Facts[Walker] = Level(10);

        await EnterAsync(harness);

        Sent<NotificationDialogMessageComposer>(harness)
            .Select(n => n.Parameters.Single(p => p.Key == "message").Value)
            .Should()
            .Equal("bienvenue");
    }

    [Fact]
    public async Task OnEntry_TheRoomReadsTheFacts()
    {
        RoomHarness harness = await ZoneAsync(ZoneSamples.Minimal());
        harness.Facts[Walker] = Level(4);

        await EnterAsync(harness);

        Held(harness).Level.Should().Be(4);
    }

    [Fact]
    public async Task AnUpdate_ReplacesThem()
    {
        RoomHarness harness = await ZoneAsync(ZoneSamples.Minimal());
        harness.Facts[Walker] = Level(4);
        await EnterAsync(harness);

        await harness.Grain.UpdatePlayerFactsAsync(Walker, Level(5, ("item.key", 1)));

        Held(harness).Level.Should().Be(5);
        Held(harness).Items["item.key"].Should().Be(1);
    }

    /// <summary>Two hops, no promise of order: a reading older than the one held arrived late.</summary>
    [Fact]
    public async Task AnOlderUpdate_ArrivingLate_IsDropped()
    {
        RoomHarness harness = await ZoneAsync(ZoneSamples.Minimal());
        harness.Facts[Walker] = Level(1);
        CharacterZoneFacts older = Level(2);
        CharacterZoneFacts newer = Level(3);
        await EnterAsync(harness);

        await harness.Grain.UpdatePlayerFactsAsync(Walker, newer);
        await harness.Grain.UpdatePlayerFactsAsync(Walker, older);

        Held(harness).Level.Should().Be(3);
    }

    [Fact]
    public async Task OnExit_TheyAreForgotten()
    {
        RoomHarness harness = await ZoneAsync(ZoneSamples.Minimal());
        harness.Facts[Walker] = Level(4);
        await EnterAsync(harness);

        await harness.Grain.RemoveAvatarFromPlayerAsync(
            harness.ContextFor(Walker),
            Walker,
            default
        );

        harness.Grain._state.ZoneRuntime!.Facts.Should().NotContainKey(Walker);
    }

    [Fact]
    public async Task AnUpdateForAPlayerWhoLeft_IsIgnored()
    {
        RoomHarness harness = await ZoneAsync(ZoneSamples.Minimal());

        await harness.Grain.UpdatePlayerFactsAsync(new PlayerId(99), Level(9));

        harness.Grain._state.ZoneRuntime!.Facts.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutACharacter_NoFactsAndNoFault()
    {
        RoomHarness harness = await ZoneAsync(ZoneSamples.Minimal());

        await EnterAsync(harness);

        harness.Grain._state.ZoneRuntime!.Facts.Should().BeEmpty();
        AvatarOf(harness).Should().NotBeNull();
    }

    [Fact]
    public async Task AFailedRead_StillLetsThePlayerIn()
    {
        RoomHarness harness = await ZoneAsync(ZoneSamples.Minimal());
        harness.CharacterFails = true;

        await EnterAsync(harness);

        AvatarOf(harness).Should().NotBeNull();
        harness.Grain._state.ZoneRuntime!.Facts.Should().BeEmpty();
    }
}

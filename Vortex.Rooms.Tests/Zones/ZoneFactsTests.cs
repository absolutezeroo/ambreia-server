using System.Collections.Immutable;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Rules;
using Vortex.Rooms.Tests.Support;
using Vortex.Rpg.Characters.Zones;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;
using static Vortex.Rooms.Tests.Zones.ZoneVisits;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// Spec Z1 §4.3: the zone keeps what it knows of each player's character — read on entry, replaced
/// on each change the character pushes, forgotten on exit.
/// </summary>
public sealed class ZoneFactsTests
{
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

        return new CharacterZoneFacts { Level = level, Items = owned.ToImmutable() };
    }

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

    [Fact]
    public async Task OnEntry_TheRoomReadsTheFacts()
    {
        RoomHarness harness = await ZoneAsync(ZoneSamples.Minimal());
        harness.Facts[Walker] = Level(4);

        await EnterAsync(harness);

        harness.Grain._state.ZoneRuntime!.Facts[Walker].Level.Should().Be(4);
    }

    [Fact]
    public async Task AnUpdate_ReplacesThem()
    {
        RoomHarness harness = await ZoneAsync(ZoneSamples.Minimal());
        harness.Facts[Walker] = Level(4);
        await EnterAsync(harness);

        await harness.Grain.UpdatePlayerFactsAsync(Walker, Level(5, ("item.key", 1)));

        harness.Grain._state.ZoneRuntime!.Facts[Walker].Level.Should().Be(5);
        harness.Grain._state.ZoneRuntime.Facts[Walker].Items["item.key"].Should().Be(1);
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
}

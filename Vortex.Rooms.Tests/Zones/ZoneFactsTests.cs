using System.Collections.Immutable;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Rooms.Tests.Support;
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

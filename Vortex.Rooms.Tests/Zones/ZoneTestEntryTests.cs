using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// The map editor's test mode (plan D18): the next sign-in of the tester starts in the zone under
/// test, on the cell they picked, once, and only for a while.
/// </summary>
public sealed class ZoneTestEntryTests
{
    private static readonly PlayerId Tester = new(9);

    private readonly List<(long Room, PlayerId Player, int X, int Y)> _cells = [];

    private ZoneTestGrain Grain(int timeoutMs = 120000) =>
        GrainActivationContext.CreateWithIntegerKey<ZoneTestGrain>(
            0,
            Options.Create(new RoomConfig { ZoneTestEntryTimeoutMs = timeoutMs }),
            FakeProxy.Create<IGrainFactory>(call =>
                call.Method.Name == nameof(IGrainFactory.GetGrain)
                && call.Method.GetGenericArguments()[0] == typeof(IRoomZone)
                    ? FakeProxy.Create<IRoomZone>(inner =>
                    {
                        _cells.Add(
                            (
                                (long)call.Args![0]!,
                                (PlayerId)inner.Args![0]!,
                                (int)inner.Args![1]!,
                                (int)inner.Args![2]!
                            )
                        );

                        return null;
                    })
                    : null
            ),
            NullLogger<ZoneTestGrain>.Instance
        );

    [Fact]
    public async Task TheNextSignIn_StartsInTheZoneUnderTest_Once()
    {
        ZoneTestGrain grain = Grain();

        await grain.ExpectAsync(Tester, new RoomId(77), -1, -1);

        (await grain.TakeAsync(Tester)).Should().Be(new RoomId(77));
        (await grain.TakeAsync(Tester)).Should().BeNull();
        _cells.Should().BeEmpty();
    }

    [Fact]
    public async Task APickedCell_IsAnnouncedToTheZone_WhenTheTesterSignsIn()
    {
        // Announced at sign-in, not when the ticket is made: the room forgets an expected arrival
        // after ExpectedArrivalTimeoutMs, and a client can take longer than that to load.
        ZoneTestGrain grain = Grain();

        await grain.ExpectAsync(Tester, new RoomId(77), 4, 1);

        _cells.Should().BeEmpty();

        await grain.TakeAsync(Tester);

        _cells.Should().Equal((77L, Tester, 4, 1));
    }

    [Fact]
    public async Task AStaleTest_IsForgotten()
    {
        ZoneTestGrain grain = Grain(timeoutMs: -1);

        await grain.ExpectAsync(Tester, new RoomId(77), -1, -1);

        (await grain.TakeAsync(Tester)).Should().BeNull();
    }
}

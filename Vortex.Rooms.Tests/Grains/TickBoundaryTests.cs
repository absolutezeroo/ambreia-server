using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Pets.Providers;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Primitives.Sound.Providers;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Grains;

/// <summary>
///     Every tick clock in the room used to catch up by stepping — <c>while (now >= next) next +=
///     tick</c> — which is exact and cheap for the millisecond drift a running room sees, and
///     neither when the gap is large. A paused room or a jumped clock turned it into millions of
///     iterations inside the grain's single turn, with the whole room queued behind it.
///     <para>
///     The replacement has to land on exactly the same boundaries as the loop it replaces, or every
///     clock in the room quietly shifts off its grid. That equivalence is what these check, against
///     the loop itself rather than against hand-written expectations.
///     </para>
/// </summary>
public sealed class TickBoundaryTests
{
    private const int ROOM_ID = 77;

    /// <summary>The stepping loop, kept here as the reference the fast path must agree with.</summary>
    private static long AdvanceByStepping(long start, long now, int tickMs)
    {
        long next = start;

        while (now >= next)
        {
            next += tickMs;
        }

        return next;
    }

    [Theory]
    [InlineData(0, 0, 500)]
    [InlineData(0, 1, 500)]
    [InlineData(0, 499, 500)]
    [InlineData(0, 500, 500)]
    [InlineData(0, 501, 500)]
    [InlineData(0, 4_999, 50)]
    [InlineData(0, 1_000_000, 50)]
    [InlineData(0, 86_400_000, 1_000)]
    public void TheFastPath_LandsWhereTheLoopWouldHave(long epoch, long now, int tickMs)
    {
        RoomGrain grain = NewGrain(epoch);

        long stepped = AdvanceByStepping(epoch, now, tickMs);
        long snapped = grain.AdvanceBoundaryPast(now, tickMs);

        snapped.Should().Be(stepped);
    }

    [Fact]
    public void ABoundaryAlwaysMovesPastTheInstantItFired()
    {
        // A clock that answered "now" for a now that is itself a boundary would run the same tick
        // again on the next pass.
        RoomGrain grain = NewGrain(epoch: 0);

        grain.AdvanceBoundaryPast(1_000, 1_000).Should().Be(2_000);
    }

    [Fact]
    public void ADayLongGap_IsClosedWithoutWalkingEveryTickInBetween()
    {
        RoomGrain grain = NewGrain(epoch: 0);

        Stopwatch stopwatch = Stopwatch.StartNew();

        // 50ms ticks across a day is 1.7 million steps for the loop, and one subtraction here.
        long next = grain.AdvanceBoundaryPast(86_400_000, 50);

        stopwatch.Stop();

        next.Should().Be(86_400_050);
        stopwatch
            .ElapsedMilliseconds.Should()
            .BeLessThan(50, "closing the gap must not depend on how large it is");
    }

    private static RoomGrain NewGrain(long epoch)
    {
        RoomGrain grain = GrainActivationContext.CreateWithIntegerKey<RoomGrain>(
            ROOM_ID,
            FakeProxy.Create<IDbContextFactory<VortexDbContext>>(_ => null),
            FakeProxy.Create<IFurnitureDefinitionProvider>(_ => null),
            FakeProxy.Create<IStuffDataFactory>(_ => null),
            Options.Create(new RoomConfig()),
            NullLogger<IRoomGrain>.Instance,
            FakeProxy.Create<IRoomModelProvider>(_ => null),
            FakeProxy.Create<IRoomItemsProvider>(_ => null),
            FakeProxy.Create<IRoomObjectLogicProvider>(_ => null),
            FakeProxy.Create<IRoomAvatarProvider>(_ => null),
            RoomGrainStubs.NoListeners(),
            FakeProxy.Create<IGrainFactory>(_ => null),
            FakeProxy.Create<IEventPublisher>(_ => null),
            RoomGrainStubs.NeverCancels(),
            FakeProxy.Create<IPermissionService>(_ => null),
            FakeProxy.Create<IVortexMetrics>(_ => null),
            FakeProxy.Create<IRoomModerationStore>(_ => null),
            FakeProxy.Create<IPetLevelProvider>(_ => null),
            FakeProxy.Create<IPetCommandProvider>(_ => null),
            FakeProxy.Create<IPetVocalProvider>(_ => null),
            FakeProxy.Create<ISongProvider>(_ => null),
            TestRoomGames.Provider(),
            FakeProxy.Create<ICommerceJournal>(_ => Task.CompletedTask),
            RoomGrainStubs.PassThroughWordFilter()
        );

        grain._state.EpochMs = epoch;

        return grain;
    }
}

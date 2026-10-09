using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Grains;
using Vortex.Primitives.World.Layers;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains;
using Vortex.Rooms.Providers;
using Vortex.Rooms.Tests.Support;
using Vortex.Rpg.Zones;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;
using static Vortex.Rooms.Tests.Zones.ZoneVisits;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// Entering a dungeon (plan D16, D21): the template's room sends each group to its own copy, and
/// whatever arrival was expected follows the player there. A world zone stays shared.
/// </summary>
public sealed class ZoneInstanceEntryTests
{
    private static readonly RoomId Template = new(RoomHarness.RoomIdValue);
    private static readonly RoomId Copy = ZoneInstanceIds.Of(Template, 3)!.Value;

    private static ZoneDocument Crypt(ZoneType type = ZoneType.Dungeon) =>
        ZoneSamples.Minimal("crypt") with
        {
            Settings = new ZoneSettings
            {
                Id = "crypt",
                Name = "Crypte",
                Type = type,
            },
            Entries = [Entry("gate", ZoneEntryKinds.DungeonEntry, 3, 3)],
        };

    [Fact]
    public async Task ADungeon_SendsThePlayerToTheirGroupsCopy()
    {
        RoomHarness harness = await ZoneAsync(Crypt());
        harness.InstanceToGive = Copy;

        (await harness.Grain.InstanceForAsync(Walker)).Should().Be(Copy);
        harness.InstanceRequests.Should().Equal((Template, "player:7"));
    }

    [Fact]
    public async Task TheExpectedArrival_FollowsThePlayerIntoTheCopy()
    {
        RoomHarness harness = await ZoneAsync(Crypt());
        harness.InstanceToGive = Copy;
        await harness.Grain.ExpectArrivalAsync(Walker, "gate");

        await harness.Grain.InstanceForAsync(Walker);

        harness.ExpectedArrivals.Should().Equal((Copy, Walker, "gate"));
    }

    [Fact]
    public async Task AWorldZone_IsShared()
    {
        RoomHarness harness = await ZoneAsync(Crypt(ZoneType.World));

        (await harness.Grain.InstanceForAsync(Walker)).Should().Be(Template);
        harness.InstanceRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenEveryCopyIsTaken_ThereIsNoRoom()
    {
        RoomHarness harness = await ZoneAsync(Crypt());
        harness.InstanceToGive = null;

        (await harness.Grain.InstanceForAsync(Walker)).Should().BeNull();
    }

    [Fact]
    public async Task ACopy_ReadsItsTemplatesZone()
    {
        RoomId asked = RoomId.Invalid;
        RoomGrain copy = Copy_(zoneAsked => asked = zoneAsked);

        await copy.ApplyZoneAsync(CancellationToken.None);

        asked.Should().Be(Template);
        (await copy.InstanceForAsync(Walker))
            .Should()
            .Be(Copy, "its group is already where it goes");
    }

    [Fact]
    public async Task ACopy_TurnsAwayAnotherGroup()
    {
        RoomGrain copy = Copy_(_ => { });

        (await copy.InstanceForAsync(new PlayerId(8)))
            .Should()
            .BeNull("a copy id is easy to guess, and the run is the group's");
    }

    /// <summary>The copy's room, whose instance directory says it belongs to the walker's group.</summary>
    private static RoomGrain Copy_(System.Action<RoomId> zoneAsked) =>
        GrainActivationContext.CreateWithIntegerKey<RoomGrain>(
            Copy.Value,
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
            FakeProxy.Create<IGrainFactory>(call =>
                call.Method.Name == nameof(IGrainFactory.GetGrain)
                && call.Method.GetGenericArguments()[0] == typeof(IZoneInstanceGrain)
                    ? FakeProxy.Create<IZoneInstanceGrain>(inner =>
                        Task.FromResult(
                            (string)inner.Args![1]! == ZoneInstanceGroups.KeyFor(Walker)
                        )
                    )
                    : null
            ),
            FakeProxy.Create<IEventPublisher>(_ => null),
            RoomGrainStubs.NeverCancels(),
            FakeProxy.Create<IPermissionService>(_ => null),
            FakeProxy.Create<IVortexMetrics>(_ => null),
            FakeProxy.Create<IZoneRoomSource>(call =>
            {
                zoneAsked((RoomId)call.Args![0]!);

                return Task.FromResult<ZoneDocument?>(null);
            }),
            FakeProxy.Create<IZoneSimulationFactory>(_ => NoZoneSimulation.Instance)
        );
}

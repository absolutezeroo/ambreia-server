using System;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Rooms.Grains.Zones.Runtime;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// Where a player lands in a zone (plan D3): the entry a passage or a teleport asked for, else
/// the zone's "arrival" entry, else the door — never a cell they cannot stand on.
/// </summary>
public sealed class ZoneArrivalTests
{
    private static readonly PlayerId Player = new(7);

    [Fact]
    public async Task AnAvatar_ArrivesOnTheZonesArrivalEntry()
    {
        RoomHarness harness = await ZoneAsync(Entry("gate", ZoneEntryKinds.Arrival, 3, 3));

        IRoomAvatar avatar = await EnterAsync(harness);

        (avatar.X, avatar.Y).Should().Be((3, 3));
    }

    [Fact]
    public async Task AnExpectedEntry_WinsOverTheDefault_Once()
    {
        RoomHarness harness = await ZoneAsync(
            Entry("gate", ZoneEntryKinds.Arrival, 3, 3),
            Entry("back", ZoneEntryKinds.Respawn, 1, 4)
        );

        await harness.Grain.ExpectArrivalAsync(Player, "back");

        IRoomAvatar first = await EnterAsync(harness);

        (first.X, first.Y).Should().Be((1, 4));

        await harness.Grain.RemoveAvatarFromPlayerAsync(
            harness.ContextFor(Player),
            Player,
            default
        );

        IRoomAvatar again = await EnterAsync(harness);

        (again.X, again.Y).Should().Be((3, 3));
    }

    [Fact]
    public async Task AnEntryOnAHole_FallsBackToTheDoor()
    {
        ZoneDocument doc = ZoneSamples.Minimal() with
        {
            Entries = [Entry("gate", ZoneEntryKinds.Arrival, 3, 3)],
        };
        doc.Floor.Tiles[3 * 6 + 3] = new ZoneTile { Hole = true };
        RoomHarness harness = await RoomHarness.CreateZoneAsync(doc);
        harness.LogicFactory = () => FakeProxy.Create<IRoomPlayerLogic>(_ => null);

        IRoomAvatar avatar = await EnterAsync(harness);

        (avatar.X, avatar.Y).Should().Be((0, 2));
    }

    [Fact]
    public async Task AnExpectedCell_WinsOnce()
    {
        RoomHarness harness = await ZoneAsync(Entry("gate", ZoneEntryKinds.Arrival, 3, 3));

        await harness.Grain.ExpectArrivalAtAsync(Player, 4, 1);

        IRoomAvatar avatar = await EnterAsync(harness);

        (avatar.X, avatar.Y).Should().Be((4, 1));
    }

    [Fact]
    public async Task AFullZone_TurnsTheExpectedArrivalAway()
    {
        RoomHarness harness = await ZoneAsync(Entry("gate", ZoneEntryKinds.Arrival, 3, 3));
        harness.Grain._state.RoomSnapshot = harness.Grain._state.RoomSnapshot with
        {
            PlayersMax = 1,
        };
        await EnterAsync(harness);

        (await harness.Grain.ExpectArrivalAsync(new PlayerId(8), "gate")).Should().BeFalse();
    }

    [Fact]
    public async Task AnExpectationNobodyHonoured_IsDroppedLater()
    {
        // A passage announced a player who never came: the room does not keep them forever.
        RoomHarness harness = await ZoneAsync(Entry("gate", ZoneEntryKinds.Arrival, 3, 3));
        ZoneRuntimeState zone = harness.Grain._state.ZoneRuntime!;
        await harness.Grain.ExpectArrivalAsync(new PlayerId(8), "gate");
        zone.ExpectedArrivals[new PlayerId(8)] = zone.ExpectedArrivals[new PlayerId(8)] with
        {
            ExpiresAtMs = 0,
        };

        await harness.Grain.ExpectArrivalAsync(new PlayerId(9), "gate");

        zone.ExpectedArrivals.Keys.Should().Equal(new PlayerId(9));
    }

    [Fact]
    public async Task AnUnknownExpectedEntry_IsTheDefaultArrival()
    {
        RoomHarness harness = await ZoneAsync(Entry("gate", ZoneEntryKinds.Arrival, 3, 3));

        await harness.Grain.ExpectArrivalAsync(Player, "nowhere");

        IRoomAvatar avatar = await EnterAsync(harness);

        (avatar.X, avatar.Y).Should().Be((3, 3));
    }

    private static async Task<RoomHarness> ZoneAsync(params ZoneEntry[] entries)
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(
            ZoneSamples.Minimal() with
            {
                Entries = [.. entries],
            }
        );

        harness.LogicFactory = () => FakeProxy.Create<IRoomPlayerLogic>(_ => null);

        return harness;
    }

    private static ZoneEntry Entry(string id, string kind, int x, int y) =>
        new()
        {
            Id = id,
            Kind = kind,
            Cell = new ZoneCell(x, y),
        };

    private static async Task<IRoomAvatar> EnterAsync(RoomHarness harness)
    {
        await harness.Grain.CreateAvatarFromPlayerAsync(
            harness.ContextFor(Player),
            new PlayerSummarySnapshot
            {
                PlayerId = Player,
                Name = "Walker",
                Motto = string.Empty,
                Figure = "hd-180-1",
                Gender = AvatarGenderType.Male,
                AchievementScore = 0,
                CreatedAt = DateTime.UtcNow,
            },
            default
        );

        return harness.Grain._state.AvatarsByObjectId[
            harness.Grain._state.AvatarsByPlayerId[Player]
        ];
    }
}

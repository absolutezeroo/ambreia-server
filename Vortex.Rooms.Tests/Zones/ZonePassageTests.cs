using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Protocol.Messages.Outgoing.Room.Session;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;
using static Vortex.Rooms.Tests.Zones.ZoneVisits;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// Passages (plan D8, D9): stepping on one sends the player to the destination's entry — another
/// zone through RoomForward with the arrival announced first, the same zone on the spot.
/// </summary>
public sealed class ZonePassageTests
{
    private static readonly RoomId Forest = new(77);

    private static ZonePassage Passage(string zone, string entry) =>
        new()
        {
            Id = "road",
            Area = [new ZoneCell(5, 2)],
            DestinationZone = zone,
            DestinationEntry = entry,
        };

    [Fact]
    public async Task APassageToAnotherZone_AnnouncesTheArrivalThenForwards()
    {
        RoomHarness harness = await ZoneAsync(
            ZoneSamples.Minimal() with
            {
                Passages = [Passage("forest", "gate")],
            }
        );
        harness.ZoneRooms["forest"] = Forest;
        await EnterAsync(harness);

        await WalkAsync(harness, 5, 2);

        harness.ExpectedArrivals.Should().Equal((Forest, Walker, "gate"));
        Sent<RoomForwardMessageComposer>(harness).Select(f => f.RoomId).Should().Equal(Forest);
    }

    [Fact]
    public async Task ASlowJourney_DoesNotHoldTheRoomsTick()
    {
        RoomHarness harness = await ZoneAsync(
            ZoneSamples.Minimal() with
            {
                Passages = [Passage("forest", "gate")],
            }
        );
        harness.ZoneLookup = new TaskCompletionSource<RoomId?>();
        await EnterAsync(harness);

        Task walk = WalkAsync(harness, 5, 2);

        (await Task.WhenAny(walk, Task.Delay(5000)))
            .Should()
            .Be(walk, "the database answering late must not stop every avatar in the room");

        harness.ZoneLookup.SetResult(Forest);
        await Task.Delay(50);

        Sent<RoomForwardMessageComposer>(harness).Select(f => f.RoomId).Should().Equal(Forest);
    }

    [Fact]
    public async Task APassageToAFullZone_KeepsThePlayerAndSaysSo()
    {
        RoomHarness harness = await ZoneAsync(
            ZoneSamples.Minimal() with
            {
                Passages = [Passage("forest", "gate")],
            }
        );
        harness.ZoneRooms["forest"] = Forest;
        harness.FullZones.Add(Forest);
        await EnterAsync(harness);

        await WalkAsync(harness, 5, 2);

        Sent<RoomForwardMessageComposer>(harness).Should().BeEmpty();
        Sent<NotificationDialogMessageComposer>(harness)
            .Should()
            .ContainSingle()
            .Which.Parameters.Single()
            .Value.Should()
            .Contain("pleine");
    }

    [Fact]
    public async Task APassageInsideTheZone_MovesThePlayerToTheEntry()
    {
        RoomHarness harness = await ZoneAsync(
            ZoneSamples.Minimal() with
            {
                Passages = [Passage("village", "back")],
                Entries = [Entry("back", ZoneEntryKinds.Respawn, 1, 4)],
            }
        );
        await EnterAsync(harness);

        await WalkAsync(harness, 5, 2);

        (AvatarOf(harness).X, AvatarOf(harness).Y).Should().Be((1, 4));
        Sent<RoomForwardMessageComposer>(harness).Should().BeEmpty();
    }

    [Fact]
    public async Task APassageToAnUnpublishedZone_KeepsThePlayerAndSaysSo()
    {
        RoomHarness harness = await ZoneAsync(
            ZoneSamples.Minimal() with
            {
                Passages = [Passage("forest", "gate")],
            }
        );
        await EnterAsync(harness);

        await WalkAsync(harness, 5, 2);

        Sent<RoomForwardMessageComposer>(harness).Should().BeEmpty();
        Sent<NotificationDialogMessageComposer>(harness).Should().ContainSingle();
        harness.ExpectedArrivals.Should().BeEmpty();
    }

    [Fact]
    public async Task ArrivingOnAPassage_DoesNotTakeIt()
    {
        RoomHarness harness = await ZoneAsync(
            ZoneSamples.Minimal() with
            {
                Passages = [Passage("forest", "gate")],
                Entries = [Entry("gate", ZoneEntryKinds.Arrival, 5, 2)],
            }
        );
        harness.ZoneRooms["forest"] = Forest;

        await EnterAsync(harness);
        await harness.TickAvatarsAsync(4);

        Sent<RoomForwardMessageComposer>(harness).Should().BeEmpty();
    }

    [Fact]
    public async Task APassageCooldown_OutlivesLeavingTheRoom()
    {
        RoomHarness harness = await ZoneAsync(
            ZoneSamples.Minimal() with
            {
                Passages = [Passage("forest", "gate") with { CooldownSeconds = 600 }],
            }
        );
        harness.ZoneRooms["forest"] = Forest;
        await EnterAsync(harness);
        await WalkAsync(harness, 5, 2);

        await harness.Grain.RemoveAvatarFromPlayerAsync(
            harness.ContextFor(Walker),
            Walker,
            default
        );
        await EnterAsync(harness);
        await WalkAsync(harness, 4, 2);
        await WalkAsync(harness, 5, 2);

        Sent<RoomForwardMessageComposer>(harness).Should().ContainSingle();
    }

    [Fact]
    public async Task APassageWithAConditionNoOneCanCheckYet_StaysClosed()
    {
        RoomHarness harness = await ZoneAsync(
            ZoneSamples.Minimal() with
            {
                Passages =
                [
                    Passage("forest", "gate") with
                    {
                        Conditions = [new PlayerLevelCondition(5, 99)],
                    },
                ],
            }
        );
        harness.ZoneRooms["forest"] = Forest;
        await EnterAsync(harness);

        await WalkAsync(harness, 5, 2);

        Sent<RoomForwardMessageComposer>(harness).Should().BeEmpty();
    }
}

using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rooms.Events.Zone;
using Vortex.Primitives.World;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;
using static Vortex.Rooms.Tests.Zones.ZoneVisits;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// Areas at run time (plan D5–D7): stepping in or out is a room event, and a named area greets the
/// player with its name and its rules.
/// </summary>
public sealed class ZoneAreaTests
{
    private static ZoneDocument Market() =>
        ZoneSamples.Minimal() with
        {
            Zones = [Area("market", "Safe", "Marché", 3, 5)],
        };

    [Fact]
    public async Task WalkingIntoANamedArea_IsAnEventAndABubble()
    {
        RoomHarness harness = await ZoneAsync(Market());
        await EnterAsync(harness);

        await WalkAsync(harness, 4, 2);

        harness
            .RoomEvents.OfType<ZoneAreaEnteredEvent>()
            .Select(e => e.AreaId)
            .Should()
            .Equal("market");
        Sent<NotificationDialogMessageComposer>(harness)
            .Should()
            .ContainSingle()
            .Which.Parameters.Single(p => p.Key == "message")
            .Value.Should()
            .Contain("Marché")
            .And.Contain("Zone sûre");
    }

    [Fact]
    public async Task WalkingOut_IsTheLeaveEvent()
    {
        RoomHarness harness = await ZoneAsync(Market());
        await EnterAsync(harness);
        await WalkAsync(harness, 4, 2);

        await WalkAsync(harness, 1, 2);

        harness
            .RoomEvents.OfType<ZoneAreaLeftEvent>()
            .Select(e => e.AreaId)
            .Should()
            .Equal("market");
    }

    [Fact]
    public async Task ArrivingInsideAnArea_EntersIt()
    {
        RoomHarness harness = await ZoneAsync(
            Market() with
            {
                Entries = [Entry("gate", ZoneEntryKinds.Arrival, 4, 4)],
            }
        );

        await EnterAsync(harness);

        harness
            .RoomEvents.OfType<ZoneAreaEnteredEvent>()
            .Select(e => e.AreaId)
            .Should()
            .Equal("market");
    }

    [Fact]
    public async Task WalkingWithinAnArea_IsNotANewEntry()
    {
        RoomHarness harness = await ZoneAsync(Market());
        await EnterAsync(harness);
        await WalkAsync(harness, 3, 2);

        await WalkAsync(harness, 5, 4);

        harness.RoomEvents.OfType<ZoneAreaEnteredEvent>().Should().ContainSingle();
    }
}

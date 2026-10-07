using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Room.Session;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// A publication reloads the zone's room: occupants are forwarded back into the same room, and the
/// activation goes so the next one loads the new version (spec §6.7, v1: back in by the door).
/// </summary>
public sealed class ZoneReloadTests
{
    [Fact]
    public async Task ReloadingAnOccupiedZone_ForwardsEveryOccupant()
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(ZoneSamples.Minimal());
        harness.PutRealPlayerOnTile(new PlayerId(7), 1, 1);
        harness.PutRealPlayerOnTile(new PlayerId(8), 2, 2);

        await harness.Grain.ReloadZoneAsync();

        harness
            .ComposersSent.Select(s => (s.Target, s.Composer.GetType()))
            .Should()
            .BeEquivalentTo([
                (new PlayerId(7), typeof(RoomForwardMessageComposer)),
                (new PlayerId(8), typeof(RoomForwardMessageComposer)),
            ]);
        harness.Deactivations.Should().Be(1);
    }

    [Fact]
    public async Task ReloadingAnEmptyZone_OnlyDeactivates()
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(ZoneSamples.Minimal());

        await harness.Grain.ReloadZoneAsync();

        harness.ComposersSent.Should().BeEmpty();
        harness.Deactivations.Should().Be(1);
    }
}

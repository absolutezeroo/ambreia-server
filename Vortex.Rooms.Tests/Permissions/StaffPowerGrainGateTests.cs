using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Events;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Permissions;

/// <summary>
/// The two staff powers no in-room controller level can gate — the room tool's checkboxes and the
/// navigator's staff pick. Both are members of public grain interfaces, and both used to be
/// authorized by their packet handler alone: a handler is a convenience, not a security boundary,
/// because anything in the cluster that can name the room can call the grain directly
/// (ROOMG-GATE-038).
/// </summary>
public sealed class StaffPowerGrainGateTests
{
    [Fact]
    public async Task TheRoomTool_RefusesAnActorWithoutModerateAny()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        await ApplyRoomToolAsync(harness).ConfigureAwait(true);

        // The room tool announces itself whenever it runs, whether or not a checkbox changed
        // anything, so this is what says the call reached the work rather than the gate.
        harness.PublishedEvents.OfType<RoomModeratedByStaffEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task TheRoomTool_RunsForAnActorWithModerateAny()
    {
        RoomHarness harness = await RoomHarness
            .CreateAsync(capabilities: [Capabilities.Room.ModerateAny])
            .ConfigureAwait(true);

        await ApplyRoomToolAsync(harness).ConfigureAwait(true);

        harness.PublishedEvents.OfType<RoomModeratedByStaffEvent>().Should().ContainSingle();
    }

    private static Task<bool> ApplyRoomToolAsync(RoomHarness harness) =>
        harness.Grain.ApplyStaffRoomActionsAsync(
            RoomHarness.Stranger,
            unlockDoor: true,
            resetNameAndDescription: false,
            kickUsers: false,
            CancellationToken.None
        );
}

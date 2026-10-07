using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rooms.Object;
using Vortex.Rooms.Object.Avatars.Player;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Mapping;

/// <summary>
/// Whether one avatar blocks another, and which way round the room setting reads.
///
/// Nothing covered this, which is how it came to be inverted and stay inverted: rooms let everybody
/// walk through everybody by default, the opposite of the setting they were created with.
///
/// The trap is a name. <c>RoomSnapshot.AllowBlocking</c> and its <c>allow_blocking</c> column hold
/// the client's <c>allowWalkThrough</c> — the handler assigns it straight in and the serializer
/// writes it straight back out, so the round trip is consistent and only the reading of it was not.
/// </summary>
public sealed class RoomWalkThroughTests
{
    private static async Task<(RoomHarness Harness, int TileId)> RoomWithSomebodyStandingAsync(
        bool allowWalkThrough
    )
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.Grain._state.RoomSnapshot = harness.Grain._state.RoomSnapshot with
        {
            AllowBlocking = allowWalkThrough,
        };

        RoomPlayerAvatar standing = harness.PutRealPlayerInRoom(2, 3, 3);
        standing.SetHeight(Altitude.Zero);

        harness.Grain.MapModule.AddAvatar(standing, false);

        int tileId = harness.Grain.MapModule.ToIdx(3, 3);

        harness.Grain.MapModule.ComputeTile(tileId);

        return (harness, tileId);
    }

    /// <summary>The default, and what a room is created with: you go round people, not through.</summary>
    [Fact]
    public async Task WithWalkThroughOff_SomebodyElsesTileIsNotAWayPast()
    {
        (RoomHarness harness, int tileId) = await RoomWithSomebodyStandingAsync(false)
            .ConfigureAwait(true);

        RoomPlayerAvatar walker = harness.PutRealPlayerInRoom(1, 0, 0);

        harness
            .Grain.MapModule.CanAvatarWalk(walker, tileId, isGoal: false)
            .Should()
            .BeFalse("walk-through is off, so the tile is occupied and not a way past");
    }

    [Fact]
    public async Task WithWalkThroughOn_SomebodyElsesTileCanBeCrossed()
    {
        (RoomHarness harness, int tileId) = await RoomWithSomebodyStandingAsync(true)
            .ConfigureAwait(true);

        RoomPlayerAvatar walker = harness.PutRealPlayerInRoom(1, 0, 0);

        harness.Grain.MapModule.CanAvatarWalk(walker, tileId, isGoal: false).Should().BeTrue();
    }

    /// <summary>Nobody stops *on* somebody else, whichever way the setting is turned.</summary>
    [Fact]
    public async Task NobodyEndsTheirWalkOnSomebodyElse()
    {
        (RoomHarness harness, int tileId) = await RoomWithSomebodyStandingAsync(true)
            .ConfigureAwait(true);

        RoomPlayerAvatar walker = harness.PutRealPlayerInRoom(1, 0, 0);

        harness.Grain.MapModule.CanAvatarWalk(walker, tileId, isGoal: true).Should().BeFalse();
    }
}

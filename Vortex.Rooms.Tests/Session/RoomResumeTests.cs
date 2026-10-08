using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Session;

/// <summary>
/// Back within the grace period (spec Personnage §10): the entry keeps the avatar only if the room
/// still holds it. A room reloaded meanwhile (a zone republished) lost it, and the player must be
/// entered afresh — kept as "resumed", they would stand in it with no avatar at all.
/// </summary>
public sealed class RoomResumeTests
{
    private static readonly RoomId Room = new(7);

    private readonly List<string> _presenceCalls = [];

    [Fact]
    public async Task AResumeIntoTheRoomStillHoldingTheAvatar_KeepsIt()
    {
        bool resumed = await RoomService.ResumeOrLeaveAsync(
            Presence(resumable: true),
            Room,
            () => Task.FromResult(true),
            CancellationToken.None
        );

        resumed.Should().BeTrue();
        _presenceCalls.Should().NotContain(nameof(IPlayerPresenceGrain.ClearActiveRoomAsync));
    }

    [Fact]
    public async Task AResumeIntoAReloadedRoom_EntersAfresh()
    {
        bool resumed = await RoomService.ResumeOrLeaveAsync(
            Presence(resumable: true),
            Room,
            () => Task.FromResult(false),
            CancellationToken.None
        );

        resumed.Should().BeFalse();
        _presenceCalls.Should().Contain(nameof(IPlayerPresenceGrain.ClearActiveRoomAsync));
    }

    [Fact]
    public async Task WithoutAResume_ThePreviousRoomIsLeft()
    {
        bool resumed = await RoomService.ResumeOrLeaveAsync(
            Presence(resumable: false),
            Room,
            () => Task.FromResult(true),
            CancellationToken.None
        );

        resumed.Should().BeFalse();
        _presenceCalls.Should().Contain(nameof(IPlayerPresenceGrain.ClearActiveRoomAsync));
    }

    private IPlayerPresenceGrain Presence(bool resumable) =>
        FakeProxy.Create<IPlayerPresenceGrain>(call =>
        {
            _presenceCalls.Add(call.Method.Name);

            return call.Method.Name == nameof(IPlayerPresenceGrain.TakeResumeAsync)
                ? Task.FromResult(resumable)
                : null;
        });
}

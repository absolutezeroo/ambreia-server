using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Players.Configuration;
using Vortex.Players.Grains;
using Vortex.Players.Tests.Support;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans.Observers;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.World;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Presence;

/// <summary>
/// Spec Personnage §10: a socket that drops while its player is in a room leaves the avatar there,
/// still, for the grace period; a new socket in time takes it back; otherwise it leaves as before.
/// </summary>
public sealed class DisconnectGraceTests
{
    private const int GraceSeconds = 60;

    private static readonly RoomId Room = new(7);

    private readonly GrainLifetimeRecorder _lifetime = new();
    private readonly List<string> _roomCalls = [];

    [Fact]
    public async Task ACutWhileInARoom_KeepsTheAvatar()
    {
        PlayerPresenceGrain grain = await InRoomAsync();

        await grain.UnregisterSessionObserverAsync(CancellationToken.None);

        _roomCalls.Should().Contain(nameof(IRoomAvatars.StopAvatarAsync));
        _roomCalls.Should().NotContain(nameof(IRoomAvatars.RemoveAvatarFromPlayerAsync));
        _lifetime.Delays.Should().Contain(TimeSpan.FromSeconds(GraceSeconds + 10));
        _lifetime.Deactivations.Should().Be(0);
        (await grain.IsOnlineAsync(CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task ReconnectingWithinGrace_ResumesTheRoom()
    {
        PlayerPresenceGrain grain = await InRoomAsync();
        await grain.UnregisterSessionObserverAsync(CancellationToken.None);

        long generation = await grain.BeginSessionAsync(CancellationToken.None);
        await grain.RegisterSessionObserverAsync(Observer(), generation);

        (await grain.GetResumableRoomAsync()).Should().Be(Room);
        (await grain.TakeResumeAsync(Room)).Should().BeTrue();
        (await grain.TakeResumeAsync(Room)).Should().BeFalse("a resume is taken once");
    }

    [Fact]
    public async Task TheGraceEnding_LeavesTheRoom()
    {
        PlayerPresenceGrain grain = await InRoomAsync();
        await grain.UnregisterSessionObserverAsync(CancellationToken.None);

        await grain.EndGraceAsync();

        _roomCalls.Should().Contain(nameof(IRoomAvatars.RemoveAvatarFromPlayerAsync));
        _lifetime.Deactivations.Should().Be(1);
        (await grain.GetResumableRoomAsync()).Should().BeNull();
    }

    [Fact]
    public async Task AGraceEndingAfterAReconnect_LeavesNothing()
    {
        PlayerPresenceGrain grain = await InRoomAsync();
        await grain.UnregisterSessionObserverAsync(CancellationToken.None);
        long generation = await grain.BeginSessionAsync(CancellationToken.None);
        await grain.RegisterSessionObserverAsync(Observer(), generation);

        await grain.EndGraceAsync();

        _roomCalls.Should().NotContain(nameof(IRoomAvatars.RemoveAvatarFromPlayerAsync));
    }

    [Fact]
    public async Task ADeactivationDuringGrace_LeavesAtOnce()
    {
        PlayerPresenceGrain grain = await InRoomAsync();
        await grain.UnregisterSessionObserverAsync(CancellationToken.None);

        await grain.OnDeactivateAsync(
            new DeactivationReason(DeactivationReasonCode.ShuttingDown, "test"),
            CancellationToken.None
        );

        _roomCalls.Should().Contain(nameof(IRoomAvatars.RemoveAvatarFromPlayerAsync));
    }

    /// <summary>The Quit path: the room was left before the socket closed, so nothing waits.</summary>
    [Fact]
    public async Task OutsideARoom_ThereIsNoGrace()
    {
        PlayerPresenceGrain grain = Build();
        await grain.RegisterSessionObserverAsync(Observer(), 1);

        await grain.UnregisterSessionObserverAsync(CancellationToken.None);

        _roomCalls.Should().NotContain(nameof(IRoomAvatars.StopAvatarAsync));
        _lifetime.Deactivations.Should().Be(1);
    }

    private async Task<PlayerPresenceGrain> InRoomAsync()
    {
        PlayerPresenceGrain grain = Build();
        await grain.RegisterSessionObserverAsync(Observer(), 1);
        grain._state.ActiveRoomId = Room;

        return grain;
    }

    private static ISessionContextObserver Observer() =>
        FakeProxy.Create<ISessionContextObserver>(_ => null);

    private PlayerPresenceGrain Build() =>
        GrainActivationContext.CreateWithIntegerKey<PlayerPresenceGrain>(
            primaryKey: 42,
            _lifetime.Observe,
            [
                FakeProxy.Create<IGrainFactory>(call =>
                    call.Method.Name != nameof(IGrainFactory.GetGrain) ? null
                    : call.Method.GetGenericArguments()[0] == typeof(IRoomAvatars)
                        ? FakeProxy.Create<IRoomAvatars>(inner =>
                        {
                            _roomCalls.Add(inner.Method.Name);

                            return Task.FromResult(true);
                        })
                    : call.Method.GetGenericArguments()[0] == typeof(IRoomDirectoryGrain)
                        ? FakeProxy.Create<IRoomDirectoryGrain>(_ => null)
                    : null
                ),
                FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
                NullLogger<PlayerPresenceGrain>.Instance,
                FakeProxy.Create<IVortexMetrics>(_ => null),
                Options.Create(new PlayerPresenceConfig { DisconnectGraceSeconds = GraceSeconds }),
                FakeProxy.Create<IPermissionService>(_ => null),
                FakeProxy.Create<IZoneRoomSource>(_ => null),
                TestChat.Pipeline(),
            ]
        );
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Players.Configuration;
using Vortex.Players.Grains;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans.Observers;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Primitives.World;
using Vortex.Protocol.Messages.Outgoing.Room.Session;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Presence;

/// <summary>
/// The way into a room (<c>PlayerPresenceGrain.EnterRoomAsync</c>): the dungeon copy redirect, the
/// room's door, and back within the grace period (spec Personnage §10) the avatar kept only if the
/// room still holds it. A room reloaded meanwhile (a zone republished) lost it, and the player must
/// be entered afresh — kept as "resumed", they would stand in it with no avatar at all.
/// </summary>
public sealed class RoomEntryTests
{
    private const int PLAYER_ID = 42;

    private static readonly RoomId Room = new(7);

    private readonly List<object> _sent = [];
    private readonly List<string> _roomCalls = [];
    private int _sentCount;
    private RoomId? _instance = Room;
    private bool _roomHoldsTheAvatar = true;
    private RoomConnectionErrorType? _refusal = RoomConnectionErrorType.NoEntry;

    [Fact]
    public async Task AResumeIntoTheRoomStillHoldingTheAvatar_KeepsIt()
    {
        PlayerPresenceGrain grain = InGrace();

        (await grain.ResumeOrLeaveAsync(Avatars(), Room, CancellationToken.None)).Should().BeTrue();
        grain._state.ActiveRoomId.Should().Be(Room);
    }

    [Fact]
    public async Task AResumeIntoAReloadedRoom_EntersAfresh()
    {
        _roomHoldsTheAvatar = false;
        PlayerPresenceGrain grain = InGrace();

        (await grain.ResumeOrLeaveAsync(Avatars(), Room, CancellationToken.None))
            .Should()
            .BeFalse();
        grain._state.ActiveRoomId.Should().Be(RoomId.Invalid);
    }

    [Fact]
    public async Task WithoutAResume_ThePreviousRoomIsLeft()
    {
        PlayerPresenceGrain grain = InGrace();
        grain._resumableRoom = -1;

        (await grain.ResumeOrLeaveAsync(Avatars(), Room, CancellationToken.None))
            .Should()
            .BeFalse();
        _roomCalls.Should().Contain(nameof(IRoomAvatars.RemoveAvatarFromPlayerAsync));
    }

    /// <summary>A refusal says why, and leaves nothing pending: the next try is not taken for a
    /// duplicate of one still in flight.</summary>
    [Fact]
    public async Task ARefusedEntry_SaysWhy_AndCanBeTriedAgain()
    {
        PlayerPresenceGrain grain = await SignedInAsync();

        await grain.EnterRoomAsync(Context(), Room, "wrong", CancellationToken.None);
        await grain.EnterRoomAsync(Context(), Room, "wrong", CancellationToken.None);

        (await SentAsync(4))
            .Select(c => c.GetType())
            .Should()
            .Equal(
                typeof(OpenConnectionMessageComposer),
                typeof(CantConnectMessageComposer),
                typeof(OpenConnectionMessageComposer),
                typeof(CantConnectMessageComposer)
            );
        _sent.OfType<CantConnectMessageComposer>().First().ErrorType.Should().Be(_refusal);
        _roomCalls.Should().Contain(nameof(IRoomCore.AdmitAsync));
    }

    [Fact]
    public async Task ADungeon_ForwardsToTheGroupsCopy()
    {
        _instance = new RoomId(99);
        PlayerPresenceGrain grain = await SignedInAsync();

        await grain.EnterRoomAsync(Context(), Room, "", CancellationToken.None);

        (await SentAsync(1))
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<RoomForwardMessageComposer>()
            .Which.RoomId.Should()
            .Be(new RoomId(99));
        _roomCalls.Should().NotContain(nameof(IRoomCore.AdmitAsync));
    }

    [Fact]
    public async Task AFullDungeon_SaysSo()
    {
        _instance = null;
        PlayerPresenceGrain grain = await SignedInAsync();

        await grain.EnterRoomAsync(Context(), Room, "", CancellationToken.None);

        (await SentAsync(1))
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new CantConnectMessageComposer { ErrorType = RoomConnectionErrorType.RoomFull }
            );
    }

    private PlayerPresenceGrain InGrace()
    {
        PlayerPresenceGrain grain = Build();
        grain._state.ActiveRoomId = Room;
        grain._resumableRoom = Room;

        return grain;
    }

    /// <summary>The grain drains its queue after a <c>Task.Yield</c>: wait (bounded) for the sends
    /// rather than read them mid-drain.</summary>
    private async Task<object[]> SentAsync(int expected)
    {
        for (int i = 0; i < 200 && Volatile.Read(ref _sentCount) < expected; i++)
        {
            await Task.Delay(5);
        }

        lock (_sent)
        {
            return [.. _sent];
        }
    }

    private async Task<PlayerPresenceGrain> SignedInAsync()
    {
        PlayerPresenceGrain grain = Build();
        await grain.RegisterSessionObserverAsync(
            FakeProxy.Create<ISessionContextObserver>(call =>
            {
                lock (_sent)
                {
                    _sent.Add(call.Args![0]!);
                }

                Interlocked.Increment(ref _sentCount);

                return null;
            }),
            1
        );

        return grain;
    }

    private static ActionContext Context() =>
        new()
        {
            Origin = ActionOrigin.Player,
            SessionKey = default,
            PlayerId = PLAYER_ID,
            RoomId = Room,
        };

    private IRoomGrain Avatars() =>
        FakeProxy.Create<IRoomGrain>(call =>
        {
            _roomCalls.Add(call.Method.Name);

            return call.Method.Name switch
            {
                nameof(IRoomAvatars.GetAllAvatarSnapshotsAsync) => Task.FromResult(
                    _roomHoldsTheAvatar
                        ? ImmutableArray.Create<RoomAvatarSnapshot>(Avatar())
                        : ImmutableArray<RoomAvatarSnapshot>.Empty
                ),
                nameof(IRoomZone.InstanceForAsync) => Task.FromResult(_instance),
                nameof(IRoomCore.AdmitAsync) => Task.FromResult(
                    new RoomAdmissionSnapshot { Refusal = _refusal }
                ),
                _ => null,
            };
        });

    private static RoomPlayerAvatarSnapshot Avatar() =>
        new()
        {
            AvatarType = default,
            WebId = PLAYER_ID,
            Name = "",
            Motto = "",
            Figure = "",
            ObjectId = default,
            X = 0,
            Y = 0,
            Z = default,
            BodyRotation = default,
            HeadRotation = default,
            Status = "",
            Gender = default,
            ActivityPoints = 0,
            IsModerator = false,
        };

    private PlayerPresenceGrain Build()
    {
        IRoomGrain room = Avatars();

        return GrainActivationContext.CreateWithIntegerKey<PlayerPresenceGrain>(
            PLAYER_ID,
            FakeProxy.Create<IGrainFactory>(call =>
                call.Method.Name != nameof(IGrainFactory.GetGrain) ? null
                : call.Method.GetGenericArguments()[0] == typeof(IRoomDirectoryGrain)
                    ? FakeProxy.Create<IRoomDirectoryGrain>(_ => null)
                : room
            ),
            FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
            NullLogger<PlayerPresenceGrain>.Instance,
            FakeProxy.Create<IVortexMetrics>(_ => null),
            Options.Create(new PlayerPresenceConfig()),
            FakeProxy.Create<IPermissionService>(_ => null),
            FakeProxy.Create<IZoneRoomSource>(_ => null)
        );
    }
}

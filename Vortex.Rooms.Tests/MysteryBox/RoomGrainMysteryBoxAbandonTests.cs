using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Primitives.Action;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.MysteryBox;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Pets.Providers;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Logic.Furniture;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Primitives.Sound.Providers;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains;
using Vortex.Rooms.Tests.Support;
using Vortex.Rooms.Wired.Logs;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.MysteryBox;

/// <summary>
/// A box is repainted to its waiting frame the moment a key holder claims it, and nothing else
/// repaints it. Every path that abandons the pairing therefore has to put the box back, or the room
/// is left with a box advertising itself as in play with no session behind it -- which is what all
/// three of these used to do by dropping the session from the dictionary directly.
/// </summary>
public sealed class RoomGrainMysteryBoxAbandonTests
{
    private const int ROOM_ID = 55;
    private const string COLOR = "purple";
    private static readonly PlayerId Owner = new(101);
    private static readonly PlayerId KeyHolder = new(202);
    private static readonly RoomObjectId BoxObjectId = new(7);

    [Fact]
    public async Task KeyHolderCancels_WithNoOwnerWaiting_PutsTheBoxBackToClosed()
    {
        Harness harness = new Harness();
        harness.StartWaitingFor(keyHolder: KeyHolder, ownerWaiting: false);

        await harness
            .Grain.CancelMysteryBoxWaitAsync(
                harness.ContextFor(KeyHolder),
                Owner,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        harness.BoxState.Should().Be(MysteryBoxSprite.ClosedState(COLOR));
        harness.HasSession.Should().BeFalse();
    }

    [Fact]
    public async Task KeyHolderLeavesTheRoom_WithNoOwnerWaiting_PutsTheBoxBackToClosed()
    {
        Harness harness = new Harness();
        harness.StartWaitingFor(keyHolder: KeyHolder, ownerWaiting: false);

        await harness
            .Grain.CancelMysteryBoxSessionsForLeavingPlayerAsync(KeyHolder)
            .ConfigureAwait(true);

        harness.BoxState.Should().Be(MysteryBoxSprite.ClosedState(COLOR));
        harness.HasSession.Should().BeFalse();
    }

    [Fact]
    public async Task KeyHolderCancels_WhileTheOwnerIsStillWaiting_LeavesTheBoxInPlay()
    {
        Harness harness = new Harness();
        harness.StartWaitingFor(keyHolder: KeyHolder, ownerWaiting: true);

        await harness
            .Grain.CancelMysteryBoxWaitAsync(
                harness.ContextFor(KeyHolder),
                Owner,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        // The owner is still standing at the box, so it stays on its waiting frame and the session
        // survives for the next key holder to join.
        harness.BoxState.Should().Be(MysteryBoxSprite.WaitingState(COLOR));
        harness.HasSession.Should().BeTrue();
    }

    [Fact]
    public async Task OwnerCancels_EndsTheSessionAndClosesTheBox()
    {
        Harness harness = new Harness();
        harness.StartWaitingFor(keyHolder: KeyHolder, ownerWaiting: true);

        await harness
            .Grain.CancelMysteryBoxWaitAsync(
                harness.ContextFor(Owner),
                Owner,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        harness.BoxState.Should().Be(MysteryBoxSprite.ClosedState(COLOR));
        harness.HasSession.Should().BeFalse();
    }

    [Fact]
    public async Task BystanderCancel_IsIgnored()
    {
        Harness harness = new Harness();
        harness.StartWaitingFor(keyHolder: KeyHolder, ownerWaiting: true);

        await harness
            .Grain.CancelMysteryBoxWaitAsync(
                harness.ContextFor(new PlayerId(999)),
                Owner,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        harness.BoxState.Should().Be(MysteryBoxSprite.WaitingState(COLOR));
        harness.HasSession.Should().BeTrue();
    }

    [Fact]
    public async Task OwnerCancels_ClosesTheKeyHoldersDialogToo()
    {
        Harness harness = new Harness();
        harness.StartWaitingFor(keyHolder: KeyHolder, ownerWaiting: true);

        await harness
            .Grain.CancelMysteryBoxWaitAsync(
                harness.ContextFor(Owner),
                Owner,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        // The owner walking away is the one case where someone else is left waiting on a box that
        // will never open, so the key holder has to be told as well as the owner.
        harness.CancelsSentTo.Should().Contain(KeyHolder).And.Contain(Owner);
    }

    [Fact]
    public async Task KeyHolderCancels_DoesNotCloseTheOwnersDialog()
    {
        Harness harness = new Harness();
        harness.StartWaitingFor(keyHolder: KeyHolder, ownerWaiting: true);

        await harness
            .Grain.CancelMysteryBoxWaitAsync(
                harness.ContextFor(KeyHolder),
                Owner,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        // The owner is still waiting for someone else to turn up; closing their dialog would end a
        // session that is still live.
        harness.CancelsSentTo.Should().Equal(KeyHolder);
    }

    private sealed class Harness
    {
        private readonly FakeFurnitureLogic _logic = new();

        public Harness()
        {
            Grain = GrainActivationContext.CreateWithIntegerKey<RoomGrain>(
                ROOM_ID,
                FakeProxy.Create<IDbContextFactory<VortexDbContext>>(_ => null),
                FakeProxy.Create<IFurnitureDefinitionProvider>(_ => null),
                FakeProxy.Create<IStuffDataFactory>(_ => null),
                Options.Create(new RoomConfig()),
                NullLogger<IRoomGrain>.Instance,
                FakeProxy.Create<IRoomModelProvider>(_ => null),
                FakeProxy.Create<IRoomItemsProvider>(_ => null),
                FakeProxy.Create<IRoomObjectLogicProvider>(_ => null),
                FakeProxy.Create<IRoomAvatarProvider>(_ => null),
                FakeProxy.Create<IRoomWiredVariablesProvider>(_ => null),
                RoomGrainStubs.NoListeners(),
                BuildGrainFactory(),
                FakeProxy.Create<IEventPublisher>(_ => null),
                RoomGrainStubs.NeverCancels(),
                FakeProxy.Create<IPermissionService>(_ => null),
                // Reports Enabled = false (the proxy's default for a bool), so the grain's timing is
                // switched off here rather than measured against a stub.
                FakeProxy.Create<IVortexMetrics>(_ => null),
                FakeProxy.Create<IRoomModerationStore>(_ => null),
                FakeProxy.Create<IPetLevelProvider>(_ => null),
                FakeProxy.Create<IPetCommandProvider>(_ => null),
                FakeProxy.Create<IPetVocalProvider>(_ => null),
                new RoomWiredLogChannel(),
                FakeProxy.Create<ISongProvider>(_ => null),
                TestRoomGames.Provider(),
                FakeProxy.Create<ICommerceJournal>(_ => Task.CompletedTask),
                RoomGrainStubs.PassThroughWordFilter()
            );

            IRoomItem box = FakeProxy.Create<IRoomItem>(call =>
                call.Method.Name switch
                {
                    $"get_{nameof(IRoomItem.Logic)}" => _logic.AsLogic(),
                    $"get_{nameof(IRoomItem.ObjectId)}" => BoxObjectId,
                    $"get_{nameof(IRoomItem.OwnerId)}" => Owner,
                    _ => null,
                }
            );

            Grain._state.ItemsById[BoxObjectId] = box;
        }

        public RoomGrain Grain { get; }

        /// <summary>Composers the grain pushed at players, so a test can tell "closed the dialog"
        /// from "left them staring at it".</summary>
        public List<PlayerId> CancelsSentTo { get; } = [];

        public int BoxState => _logic.State;

        public bool HasSession => Grain._state.MysteryBoxSessionsByOwnerId.ContainsKey(Owner);

        public ActionContext ContextFor(PlayerId playerId) =>
            new(ActionOrigin.Player, default, playerId, new RoomId(ROOM_ID));

        /// <summary>A grain factory that answers presence lookups with a recorder, so the grain's
        /// "close the dialog" pushes land somewhere a test can read.</summary>
        private IGrainFactory BuildGrainFactory() =>
            FakeProxy.Create<IGrainFactory>(call =>
            {
                if (
                    !call.Method.IsGenericMethod
                    || call.Method.GetGenericArguments()[0] != typeof(IPlayerPresenceGrain)
                )
                {
                    return null;
                }

                PlayerId target = new((int)(long)call.Args![0]!);

                return FakeProxy.Create<IPlayerPresenceGrain>(inner =>
                {
                    if (inner.Method.Name == nameof(IPlayerPresenceGrain.SendComposerAsync))
                    {
                        CancelsSentTo.Add(target);
                    }

                    return null;
                });
            });

        /// <summary>Puts the room in the state it reaches once someone has claimed the box: a live
        /// session and a box already repainted to its waiting frame.</summary>
        public void StartWaitingFor(PlayerId keyHolder, bool ownerWaiting)
        {
            _logic.State = MysteryBoxSprite.WaitingState(COLOR);

            Grain._state.MysteryBoxSessionsByOwnerId[Owner] = new RoomMysteryBoxSession
            {
                BoxObjectId = BoxObjectId,
                BoxOwnerId = Owner,
                Color = COLOR,
                StartedAtMs = 0,
                OwnerWaiting = ownerWaiting,
                KeyHolderId = keyHolder,
            };
        }
    }

    /// <summary>Records the state the grain paints, which is the whole point of these tests. The
    /// interface is far wider than the two members involved, so the rest is left to the proxy.</summary>
    private sealed class FakeFurnitureLogic
    {
        public int State { get; set; }

        public IFurnitureLogic AsLogic() =>
            FakeProxy.Create<IFurnitureLogic>(call =>
            {
                switch (call.Method.Name)
                {
                    case nameof(IFurnitureLogic.GetState):
                        return State;

                    case nameof(IFurnitureLogic.SetStateAsync):
                        State = (int)call.Args![0]!;

                        return Task.CompletedTask;

                    default:
                        return null;
                }
            });
    }
}

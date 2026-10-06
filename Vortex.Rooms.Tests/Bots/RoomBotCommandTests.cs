using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Bots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Rooms.Grains.Systems;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Bots;

/// <summary>
/// What a wired stack can tell a bot to do: go somewhere, follow somebody, change its look. These
/// are orders rather than settings — nothing but the look outlives the room — and they outrank the
/// bot's own wandering, because a builder who sent a bot somewhere should not watch it stroll off.
/// </summary>
public sealed class RoomBotCommandTests
{
    private const int BotId = 7;

    /// <summary>Enough ticks for a bot to walk the width of the test room several times over.</summary>
    private const int EnoughTicksToArrive = 60;

    private static readonly PlayerId Owner = new(101);
    private static readonly PlayerId Walker = new(202);

    [Fact]
    public async Task TeleportingABot_PutsItThereAtOnceAndTellsTheRoom()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.BroadcastToRoom.Clear();

        bool moved = await harness
            .BotSystem.TeleportAsync(BotId, 9, 9, CancellationToken.None)
            .ConfigureAwait(true);

        moved.Should().BeTrue();

        RoomBotAvatarSnapshot bot = await harness.ReadBotAvatarAsync().ConfigureAwait(true);

        bot.X.Should().Be(9);
        bot.Y.Should().Be(9);

        harness
            .BroadcastToRoom.OfType<UserUpdateMessageComposer>()
            .Should()
            .ContainSingle(
                "a teleport that nobody is told about is a bot that jumps on next redraw"
            );
    }

    [Fact]
    public async Task TeleportingOntoATileThatWillNotTakeIt_IsRefusedAndLeavesItWhereItWas()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.BlockTile(9, 9);

        bool moved = await harness
            .BotSystem.TeleportAsync(BotId, 9, 9, CancellationToken.None)
            .ConfigureAwait(true);

        moved.Should().BeFalse();

        RoomBotAvatarSnapshot bot = await harness.ReadBotAvatarAsync().ConfigureAwait(true);

        bot.X.Should().Be(3);
        bot.Y.Should().Be(4);
    }

    [Fact]
    public async Task ABotSentSomewhere_WalksThereAndThenForgetsTheOrder()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        bool ordered = await harness
            .BotSystem.WalkToAsync(BotId, 8, 8, CancellationToken.None)
            .ConfigureAwait(true);

        ordered.Should().BeTrue();

        await harness.TickAsync(EnoughTicksToArrive).ConfigureAwait(true);

        RoomBotAvatarSnapshot bot = await harness.ReadBotAvatarAsync().ConfigureAwait(true);

        bot.X.Should().Be(8);
        bot.Y.Should().Be(8);

        // Wandering is off, so once the order is spent nothing should move the bot again.
        harness.BroadcastToRoom.Clear();
        await harness.TickAsync(EnoughTicksToArrive).ConfigureAwait(true);

        harness
            .BroadcastToRoom.OfType<UserUpdateMessageComposer>()
            .Should()
            .BeEmpty("an arrived bot has no order left to walk out");
    }

    [Fact]
    public async Task AnOrderOutranksWandering()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        await harness.EnableWanderAsync().ConfigureAwait(true);

        await harness
            .BotSystem.WalkToAsync(BotId, 8, 8, CancellationToken.None)
            .ConfigureAwait(true);

        harness.BroadcastToRoom.Clear();

        await harness.TickAsync(EnoughTicksToArrive).ConfigureAwait(true);

        // It reaches the tile it was sent to, and afterwards it is free to wander off again — so
        // the walk is asserted over the whole route rather than at the end of it.
        harness
            .BroadcastToRoom.OfType<UserUpdateMessageComposer>()
            .SelectMany(update => update.Avatars)
            .OfType<RoomBotAvatarSnapshot>()
            .Select(bot => (bot.X, bot.Y))
            .Should()
            .Contain((8, 8), "a wandering bot under orders still goes where it was sent");
    }

    [Fact]
    public async Task AFollowingBot_ClosesOnItsTargetAndStopsBesideThem()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.PutPlayerInRoom(Walker, 10, 10);

        await harness
            .BotSystem.SetFollowTargetAsync(BotId, Walker, CancellationToken.None)
            .ConfigureAwait(true);

        await harness.TickAsync(EnoughTicksToArrive).ConfigureAwait(true);

        RoomBotAvatarSnapshot bot = await harness.ReadBotAvatarAsync().ConfigureAwait(true);

        Math.Max(Math.Abs(bot.X - 10), Math.Abs(bot.Y - 10))
            .Should()
            .Be(1, "a follower stops beside its target, never on top of it");
    }

    [Fact]
    public async Task ABotToldToStopFollowing_StaysWhereItIs()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.PutPlayerInRoom(Walker, 10, 10);

        await harness
            .BotSystem.SetFollowTargetAsync(BotId, Walker, CancellationToken.None)
            .ConfigureAwait(true);
        await harness.TickAsync(2).ConfigureAwait(true);

        await harness
            .BotSystem.SetFollowTargetAsync(BotId, null, CancellationToken.None)
            .ConfigureAwait(true);

        RoomBotAvatarSnapshot before = await harness.ReadBotAvatarAsync().ConfigureAwait(true);

        await harness.TickAsync(EnoughTicksToArrive).ConfigureAwait(true);

        RoomBotAvatarSnapshot after = await harness.ReadBotAvatarAsync().ConfigureAwait(true);

        (after.X, after.Y).Should().Be((before.X, before.Y));
    }

    [Fact]
    public async Task AFollowingBotWhoseTargetIsNotInTheRoom_WaitsRatherThanWalkingOff()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        // Never put the player in the room: the order stands, but there is nothing to walk towards.
        await harness
            .BotSystem.SetFollowTargetAsync(BotId, Walker, CancellationToken.None)
            .ConfigureAwait(true);

        await harness.TickAsync(EnoughTicksToArrive).ConfigureAwait(true);

        RoomBotAvatarSnapshot bot = await harness.ReadBotAvatarAsync().ConfigureAwait(true);

        (bot.X, bot.Y).Should().Be((3, 4));
    }

    [Fact]
    public async Task ABotLookChange_IsWrittenDownAndRedrawn()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.BroadcastToRoom.Clear();

        bool changed = await harness
            .BotSystem.SetFigureAsync(BotId, "hd-99-1.ch-210-66", CancellationToken.None)
            .ConfigureAwait(true);

        changed.Should().BeTrue();

        await using VortexDbContext dbCtx = harness.NewDbContext();
        BotEntity bot = await dbCtx.Bots.SingleAsync(b => b.Id == BotId).ConfigureAwait(true);

        bot.Figure.Should()
            .Be("hd-99-1.ch-210-66", "the look is the bot's own, so it is persisted");

        harness
            .BroadcastToRoom.OfType<UserChangeMessageComposer>()
            .Should()
            .ContainSingle()
            .Which.Figure.Should()
            .Be("hd-99-1.ch-210-66");
    }

    [Fact]
    public async Task AnEmptyLook_IsRefusedRatherThanLeavingTheBotInvisible()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        bool changed = await harness
            .BotSystem.SetFigureAsync(BotId, "   ", CancellationToken.None)
            .ConfigureAwait(true);

        changed.Should().BeFalse();

        await using VortexDbContext dbCtx = harness.NewDbContext();
        BotEntity bot = await dbCtx.Bots.SingleAsync(b => b.Id == BotId).ConfigureAwait(true);

        bot.Figure.Should().Be(RoomHarness.OriginalFigure);
    }

    [Fact]
    public async Task OrdersAimedAtABotThatIsNotHere_AreRefusedRatherThanThrowing()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        (
            await harness
                .BotSystem.TeleportAsync(999, 5, 5, CancellationToken.None)
                .ConfigureAwait(true)
        )
            .Should()
            .BeFalse();
        (
            await harness
                .BotSystem.WalkToAsync(999, 5, 5, CancellationToken.None)
                .ConfigureAwait(true)
        )
            .Should()
            .BeFalse();
        (
            await harness
                .BotSystem.SetFollowTargetAsync(999, Walker, CancellationToken.None)
                .ConfigureAwait(true)
        )
            .Should()
            .BeFalse();
        (
            await harness
                .BotSystem.SetFigureAsync(999, "hd-1-1", CancellationToken.None)
                .ConfigureAwait(true)
        )
            .Should()
            .BeFalse();
    }
}

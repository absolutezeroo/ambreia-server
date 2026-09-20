using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Protocol.Messages.Outgoing.Inventory.Trading;
using Vortex.Rooms.Object.Avatars.Player;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Trading;

/// <summary>
/// Which id space the trade messages speak.
/// </summary>
/// <remarks>
/// <c>OpenTrading</c> comes in naming the other avatar by its room-object id, and the server used
/// to answer in the same currency. The client does not: <c>onTradingOpen</c> resolves both sides
/// with <c>userDataManager.getUserData()</c>, which is keyed by web id, and then decides which
/// half of the window is yours with <c>otherUserId == sessionDataManager.userId</c> — a web id
/// again.
/// <para>
/// It looked correct because the room's object counter hands out 1, 2, … and a fresh hotel's
/// player ids are also 1, 2, …. These tests give the two players object ids that deliberately do
/// not match their player ids, which is what a third avatar in the room produces, and which is the
/// only condition under which the old code was visibly wrong.
/// </para>
/// </remarks>
public sealed class TradeIdSpaceTests
{
    private static readonly PlayerId Owner = RoomHarness.Owner;
    private static readonly PlayerId Guest = RoomHarness.Stranger;

    // Deliberately not PlayerId.Value: the whole point is that the two spaces differ.
    private static readonly RoomObjectId OwnerObject = new(Owner.Value + 40);
    private static readonly RoomObjectId GuestObject = new(Guest.Value + 70);

    [Fact]
    public async Task TradingOpen_NamesBothSidesByPlayerId()
    {
        RoomHarness harness = await CreateRoomWithShiftedObjectIdsAsync().ConfigureAwait(true);

        await harness
            .Grain.TradingSystem.OpenTradeAsync(Owner, GuestObject.Value, CancellationToken.None)
            .ConfigureAwait(true);

        TradingOpenEventMessageComposer open = Single<TradingOpenEventMessageComposer>(harness);

        open.UserId.Should().Be(Owner.Value, "getUserData() is keyed by web id");
        open.OtherUserId.Should().Be(Guest.Value);

        open.UserId.Should().NotBe(OwnerObject.Value);
        open.OtherUserId.Should().NotBe(GuestObject.Value);
    }

    [Fact]
    public async Task TradingAccept_NamesTheAccepterByPlayerId()
    {
        RoomHarness harness = await CreateRoomWithShiftedObjectIdsAsync().ConfigureAwait(true);

        await harness
            .Grain.TradingSystem.OpenTradeAsync(Owner, GuestObject.Value, CancellationToken.None)
            .ConfigureAwait(true);

        harness.ComposersSent.Clear();

        await harness
            .Grain.TradingSystem.SetTradeAcceptAsync(Guest, true, CancellationToken.None)
            .ConfigureAwait(true);

        TradingAcceptEventMessageComposer accept = Single<TradingAcceptEventMessageComposer>(
            harness
        );

        // The client compares this against the id TradingOpen gave it. In the other space the
        // acceptance lands on the wrong half of the window, or on neither.
        accept.UserId.Should().Be(Guest.Value);
        accept.UserAccepts.Should().BeTrue();
    }

    [Fact]
    public async Task TradingClose_NamesTheCancellerByPlayerId()
    {
        RoomHarness harness = await CreateRoomWithShiftedObjectIdsAsync().ConfigureAwait(true);

        await harness
            .Grain.TradingSystem.OpenTradeAsync(Owner, GuestObject.Value, CancellationToken.None)
            .ConfigureAwait(true);

        harness.ComposersSent.Clear();

        await harness
            .Grain.TradingSystem.CloseTradeAsync(Guest, CancellationToken.None)
            .ConfigureAwait(true);

        TradingCloseEventMessageComposer close = Single<TradingCloseEventMessageComposer>(harness);

        // "the other user cancelled" is shown when this differs from your own id, so in the wrong
        // space the canceller is told someone else cancelled and the other side is told nothing.
        close.UserId.Should().Be(Guest.Value);
    }

    private static T Single<T>(RoomHarness harness)
        where T : IComposer =>
        harness.ComposersSent.Select(sent => sent.Composer).OfType<T>().First();

    private static async Task<RoomHarness> CreateRoomWithShiftedObjectIdsAsync()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        // The harness room ships with trading off, which refuses the open before any of this.
        harness.Grain._state.RoomSnapshot = harness.Grain._state.RoomSnapshot with
        {
            TradeType = RoomTradeModeType.Everyone,
        };

        // PutRealPlayerInRoom() forces objectId == playerId, which is exactly the coincidence
        // under test, so the two avatars go into the live state by hand.
        Place(harness, Owner, OwnerObject, 2, 2);
        Place(harness, Guest, GuestObject, 3, 3);

        return harness;
    }

    private static void Place(
        RoomHarness harness,
        PlayerId playerId,
        RoomObjectId objectId,
        int x,
        int y
    )
    {
        RoomPlayerAvatar avatar = new() { ObjectId = objectId, PlayerId = playerId };
        avatar.SetPosition(x, y);

        harness.Grain._state.AvatarsByPlayerId[playerId] = objectId;
        harness.Grain._state.AvatarsByObjectId[objectId] = avatar;
    }
}

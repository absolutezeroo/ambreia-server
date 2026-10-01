using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// What a room answers when a player acts on furniture it cannot act on: a refusal, not a fault.
/// Both used to throw, which the room grain turned into <c>false</c> after logging a stack trace at
/// error level -- for a chair dropped on a wall, or a click on an item somebody else had just
/// picked up.
/// </summary>
public sealed class RefusedFurniActionTests
{
    private static readonly RoomObjectId Placed = new(81);

    [Fact]
    public async Task MovingOntoASquareTheItemCannotTake_IsRefusedWithTheHotelsBubble()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.LogicFactory = TestFloorItems.WorkingLogic;

        IRoomFloorItem item = TestFloorItems.Movable(Placed, RoomHarness.Owner);

        await harness
            .Grain.FurniModule.PlaceFloorItemAsync(
                harness.ContextFor(RoomHarness.Owner),
                item,
                5,
                6,
                Rotation.North,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        // A hole in the floor plan: the one square no item may take.
        harness.Grain._state.TileFlags[harness.Grain.MapModule.ToIdx(7, 6)] =
            RoomTileFlags.Disabled;

        bool moved = await harness
            .Grain.ActionModule.MoveFloorItemByIdAsync(
                harness.ContextFor(RoomHarness.Owner),
                Placed,
                7,
                6,
                Rotation.North,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        moved.Should().BeFalse();
        (item.X, item.Y).Should().Be((5, 6));
        harness
            .ComposersSent.Where(sent => sent.Target == RoomHarness.Owner)
            .Select(sent => sent.Composer)
            .OfType<NotificationDialogMessageComposer>()
            .Should()
            .ContainSingle(notice => notice.Type == FurniPlacementNotice.Type);
    }

    [Fact]
    public async Task ClickingAnItemTheRoomDoesNotHave_IsAQuietNo()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        bool clicked = await harness
            .Grain.FurniModule.ClickItemByIdAsync(
                harness.ContextFor(RoomHarness.Owner),
                new RoomObjectId(999),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        clicked.Should().BeFalse();
    }
}

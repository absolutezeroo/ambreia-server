using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Mapping;

/// <summary>
/// Turning an item where it stands. A player's turn measures the tile once the item is off it, so
/// the foot of a stack lands on top, as the hotel does; it used to keep its own height and stay
/// underneath. A turn not made by a player, straight on the map, keeps the height, or a piece turned
/// repeatedly at the foot of a build would climb it on every turn.
/// </summary>
public sealed class RotateInPlaceRestackTests
{
    private const int X = 5;
    private const int Y = 6;

    private static readonly RoomObjectId Bottom = new(71);
    private static readonly RoomObjectId Top = new(72);
    private static readonly Altitude TopHeight = Altitude.FromValue(1.0);

    [Fact]
    public async Task APlayerTurningTheFootOfAStack_LandsOnTopOfIt()
    {
        (RoomHarness harness, IRoomFloorItem bottom) = await StackAsync().ConfigureAwait(true);

        bool moved = await harness
            .Grain.FurniModule.MoveFloorItemByIdAsync(
                harness.ContextFor(RoomHarness.Owner),
                Bottom,
                X,
                Y,
                null,
                Rotation.East,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        moved.Should().BeTrue();
        bottom.Rotation.Should().Be(Rotation.East);
        bottom.Z.Should().Be(TopHeight);
    }

    [Fact]
    public async Task ATurnNotMadeByAPlayer_KeepsTheItemsHeight()
    {
        (RoomHarness harness, IRoomFloorItem bottom) = await StackAsync().ConfigureAwait(true);

        harness.Grain.MapModule.MoveFloorItem(
            bottom,
            harness.Grain.MapModule.ToIdx(X, Y),
            null,
            Rotation.East
        );

        bottom.Rotation.Should().Be(Rotation.East);
        bottom.Z.Should().Be(Altitude.Zero);
    }

    /// <summary>Two items on one tile, the second raised above the first.</summary>
    private static async Task<(RoomHarness, IRoomFloorItem)> StackAsync()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.LogicFactory = TestFloorItems.WorkingLogic;

        IRoomFloorItem bottom = TestFloorItems.Movable(Bottom, RoomHarness.Owner);
        IRoomFloorItem top = TestFloorItems.Movable(Top, RoomHarness.Owner);

        await PlaceAsync(harness, bottom).ConfigureAwait(true);
        await PlaceAsync(harness, top).ConfigureAwait(true);

        harness.Grain.MapModule.MoveFloorItem(top, harness.Grain.MapModule.ToIdx(X, Y), TopHeight);

        bottom.Z.Should().Be(Altitude.Zero, "the setup needs the item to start at the foot");

        return (harness, bottom);
    }

    private static Task<bool> PlaceAsync(RoomHarness harness, IRoomFloorItem item) =>
        harness.Grain.FurniModule.PlaceFloorItemAsync(
            harness.ContextFor(RoomHarness.Owner),
            item,
            X,
            Y,
            Rotation.North,
            CancellationToken.None
        );
}

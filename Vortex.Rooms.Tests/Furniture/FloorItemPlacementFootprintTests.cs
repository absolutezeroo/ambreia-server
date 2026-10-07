using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Object.Logic.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Furniture;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// Where a placed item leaves its id on the map.
/// </summary>
/// <remarks>
/// <para>
/// An item built from a <c>FurnitureItemSnapshot</c> is materialised without coordinates, and placement
/// used to attach it first: <c>AttatchObjectAsync</c> registers the footprint from the coordinates
/// the item is carrying, so every new floor item was registered at (0, 0) before the real position
/// was applied and registered a second time. Nothing took the first one back — removal clears the
/// footprint the item is standing on — so tile (0, 0) kept the id, and through it the item's height
/// and its <c>FurnitureOccupied</c> flag, for the life of the activation.
/// </para>
/// <para>
/// The fix is the order. The assertion is the invariant rather than the ordering: the tiles holding an item's id are
/// exactly its footprint, and nowhere else.
/// </para>
/// </remarks>
public sealed class FloorItemPlacementFootprintTests
{
    private const int TargetX = 5;
    private const int TargetY = 6;

    private static readonly RoomObjectId Placed = new(31);

    [Fact]
    public async Task PlacingAFloorItem_LeavesItsIdOnItsFootprintAndNowhereElse()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        IRoomFloorItem item = MovableFloorItem(harness, width: 1, length: 1);

        bool placed = await harness
            .Grain.FurniModule.PlaceFloorItemAsync(
                harness.ContextFor(RoomHarness.Owner),
                item,
                TargetX,
                TargetY,
                Rotation.North,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        placed.Should().BeTrue();

        TilesHolding(harness, Placed)
            .Should()
            .Equal(harness.Grain.MapModule.ToIdx(TargetX, TargetY));
    }

    /// <summary>
    /// The multi-tile case, which is the one that used to poison a whole corner rather than a single
    /// tile: a 2x2 registered four tiles around the origin on its way past.
    /// </summary>
    [Fact]
    public async Task PlacingAMultiTileItem_RegistersFourTilesAndOnlyThose()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        IRoomFloorItem item = MovableFloorItem(harness, width: 2, length: 2);

        await harness
            .Grain.FurniModule.PlaceFloorItemAsync(
                harness.ContextFor(RoomHarness.Owner),
                item,
                TargetX,
                TargetY,
                Rotation.North,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        List<int> footprint = TilesHolding(harness, Placed);

        footprint.Should().HaveCount(4);
        footprint.Should().Contain(harness.Grain.MapModule.ToIdx(TargetX, TargetY));
        footprint.Should().NotContain(0, "the origin is not part of this item's footprint");
    }

    private static List<int> TilesHolding(RoomHarness harness, RoomObjectId objectId) =>
        [
            .. Enumerable
                .Range(0, harness.Grain._state.TileFloorStacks.Length)
                .Where(idx => harness.Grain._state.TileFloorStacks[idx].Contains(objectId)),
        ];

    /// <summary>
    /// The logic arrives from the provider during the attach, the way a real one does — an item that
    /// turns up with its logic already on it is refused by <c>AttatchLogicAsync</c> and never reaches
    /// the map, which would make these tests pass on their own.
    /// </summary>
    private static IRoomFloorItem MovableFloorItem(RoomHarness harness, int width, int length)
    {
        harness.LogicFactory = TestFloorItems.WorkingLogic;

        return TestFloorItems.Movable(Placed, RoomHarness.Owner, width, length);
    }
}

using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// Placing an item from the inventory onto a tile somebody is standing on.
/// </summary>
/// <remarks>
/// The item being placed has no logic yet — the provider hands it one during the attach, after the
/// placement has been validated — so the validation cannot ask the logic whether it is walkable. It
/// did, and every placement onto an occupied tile died on a <c>NullReferenceException</c> instead of
/// answering. The definition is what is known about the item at that point.
/// </remarks>
public sealed class FloorItemPlacementOnAvatarTests
{
    private const int TargetX = 5;
    private const int TargetY = 6;

    private static readonly RoomObjectId Placed = new(31);

    [Fact]
    public async Task AWalkableItem_CanBePlacedUnderAnAvatar()
    {
        RoomHarness harness = await StandSomebodyOnTheTargetAsync().ConfigureAwait(true);

        IRoomFloorItem item = TestFloorItems.Movable(Placed, RoomHarness.Owner, canWalk: true);

        bool valid = await ValidateAsync(harness, item).ConfigureAwait(true);

        valid.Should().BeTrue();
    }

    [Fact]
    public async Task ASolidItem_IsRefusedUnderAnAvatar()
    {
        RoomHarness harness = await StandSomebodyOnTheTargetAsync().ConfigureAwait(true);

        IRoomFloorItem item = TestFloorItems.Movable(Placed, RoomHarness.Owner, canWalk: false);

        bool valid = await ValidateAsync(harness, item).ConfigureAwait(true);

        valid.Should().BeFalse();
    }

    private static async Task<RoomHarness> StandSomebodyOnTheTargetAsync()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.PutRealPlayerOnTile(RoomHarness.Stranger, TargetX, TargetY);
        harness.Grain.MapModule.ComputeTile(TargetX, TargetY);

        return harness;
    }

    private static Task<bool> ValidateAsync(RoomHarness harness, IRoomFloorItem item) =>
        harness.Grain.FurniModule.ValidateNewFloorItemPlacementAsync(
            harness.ContextFor(RoomHarness.Owner),
            item,
            TargetX,
            TargetY,
            Rotation.North
        );
}

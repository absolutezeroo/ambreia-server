using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// A row whose footprint overhangs the map (the model changed under it) used to abort the room's
/// furniture load: the rollback threw again on the same tiles, the item stayed in ItemsById with no
/// footprint, tile computation stayed paused, and every retry tripped over the items already
/// attached -- the room could not be entered until the silo restarted.
/// </summary>
public sealed class FurnitureLoadToleranceTests
{
    [Fact]
    public async Task AnItemOverhangingTheMap_IsLeftOut_AndTheRestOfTheRoomLoads()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        harness.LogicFactory = TestFloorItems.WorkingLogic;

        IRoomFloorItem overhanging = TestFloorItems.Movable(
            new RoomObjectId(1),
            RoomHarness.Owner,
            width: 3
        );
        overhanging.SetPosition(11, 0);

        IRoomFloorItem healthy = TestFloorItems.Movable(new RoomObjectId(2), RoomHarness.Owner);
        healthy.SetPosition(2, 2);

        harness.FloorItemsToLoad.AddRange([overhanging, healthy]);

        await harness.Grain.FurniModule.EnsureFurniLoadedAsync(CancellationToken.None);

        harness.Grain._state.IsFurniLoaded.Should().BeTrue();
        harness.Grain._state.IsTileComputationPaused.Should().BeFalse();
        harness.Grain._state.ItemsById.Should().NotContainKey(new RoomObjectId(1));
        harness.Grain._state.ItemsById.Should().ContainKey(new RoomObjectId(2));
        RoomFurnitureInvariants.Violations(harness.Grain._state).Should().BeEmpty();
    }
}

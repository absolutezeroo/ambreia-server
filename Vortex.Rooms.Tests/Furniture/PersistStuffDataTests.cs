using System.Text.Json;
using FluentAssertions;
using Vortex.Furniture;
using Vortex.Furniture.Providers;
using Vortex.Primitives.Furniture;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Furniture.Snapshots.StuffData;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Rooms.Object.Logic.Furniture.Floor;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// Furniture whose interesting state is data rather than a state number — a figure and a name in a
/// map, a free text — has no <c>SetStateAsync</c> to ride on, so it writes into
/// <c>StuffData</c> directly and then has to ask for that write to be persisted. If
/// <c>PersistStuffDataAsync</c> does not copy the data into the item's STUFF section, the change
/// lives in memory until the room grain deactivates and then vanishes with no error anywhere.
/// </summary>
public sealed class PersistStuffDataTests
{
    [Fact]
    public void PersistStuffData_WritesTheStuffSectionForPersistentFurniture()
    {
        IExtraData extraData = new ExtraData(null);
        FurnitureFloorLogic logic = new(
            new StuffDataFactory(),
            StubContext(StuffDataType.MapKey, extraData)
        );

        ((IMapStuffData)logic.StuffData).Data["FIGURE"] = "hd-180-1";

        logic.PersistStuffDataAsync(refresh: false).IsCompletedSuccessfully.Should().BeTrue();

        extraData
            .TryGetSection(ExtraDataSectionType.STUFF, out JsonElement section)
            .Should()
            .BeTrue("the write must reach the item's persisted extra data, not just memory");

        section.GetRawText().Should().Contain("hd-180-1");
    }

    [Fact]
    public void SetState_StillPersistsThroughTheSharedPath()
    {
        // SetStateAsync was refactored to call PersistStuffDataAsync rather than duplicate it;
        // this pins that the state path did not lose its persistence in the move.
        IExtraData extraData = new ExtraData(null);
        FurnitureFloorLogic logic = new(
            new StuffDataFactory(),
            StubContext(StuffDataType.LegacyKey, extraData)
        );

        logic.SetStateAsync(3, refresh: false).IsCompletedSuccessfully.Should().BeTrue();

        extraData
            .TryGetSection(ExtraDataSectionType.STUFF, out JsonElement section)
            .Should()
            .BeTrue();
        section.GetRawText().Should().Contain("3");
    }

    [Fact]
    public void PersistStuffData_RefreshesTheSnapshotAWriteWentBehind()
    {
        // The snapshot is cached and `Data` is the live dictionary, so a caller that writes a key
        // and persists used to save the new value and broadcast the old one — the furni came back
        // one edit behind for every client, with nothing thrown and nothing logged.
        IExtraData extraData = new ExtraData(null);
        FurnitureFloorLogic logic = new(
            new StuffDataFactory(),
            StubContext(StuffDataType.MapKey, extraData)
        );

        // Build the snapshot once, the way a room-entry serialization would.
        logic.StuffData.GetSnapshot();

        ((IMapStuffData)logic.StuffData).Data["chest_name"] = "after";

        logic.PersistStuffDataAsync(refresh: false).IsCompletedSuccessfully.Should().BeTrue();

        MapStuffSnapshot snapshot = (MapStuffSnapshot)logic.StuffData.GetSnapshot();

        snapshot
            .Data["chest_name"]
            .Should()
            .Be(
                "after",
                "the snapshot the client is sent has to carry the write that was persisted"
            );
    }

    private static IRoomFloorItemContext StubContext(
        StuffDataType stuffDataType,
        IExtraData extraData
    )
    {
        FurnitureDefinitionSnapshot definition = new()
        {
            Id = 1,
            SpriteId = 1,
            Name = "mannequin",
            ProductType = ProductType.Floor,
            FurniCategory = FurnitureCategory.Default,
            LogicName = "furniture_mannequin",
            TotalStates = 1,
            Width = 1,
            Length = 1,
            StackHeight = default,
            CanStack = false,
            CanWalk = false,
            CanSit = false,
            CanLay = false,
            CanRecycle = false,
            CanTrade = true,
            CanGroup = false,
            CanSell = true,
            UsagePolicy = FurnitureUsageType.Everybody,
            ExtraData = null,
            StuffDataType = stuffDataType,
        };

        IRoomFloorItem item = FakeProxy.Create<IRoomFloorItem>(call =>
            call.Method.Name == "get_ExtraData" ? extraData : null
        );

        return FakeProxy.Create<IRoomFloorItemContext>(call =>
            call.Method.Name switch
            {
                "get_Definition" => definition,
                "get_RoomObject" => item,
                _ => null,
            }
        );
    }
}

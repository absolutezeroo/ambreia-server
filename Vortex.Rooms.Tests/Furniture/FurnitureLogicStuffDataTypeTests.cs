using FluentAssertions;
using Vortex.Furniture;
using Vortex.Furniture.Providers;
using Vortex.Primitives.Furniture;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Rooms.Object.Logic.Furniture.Floor;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// A furniture's stuff data format comes from its definition, and the room has to agree with the
/// inventory about that. It did not: FurnitureLogic hardcoded the legacy format and no logic class
/// ever overrode it, so a crackable arrived in the room as a legacy bag and its counters had
/// nowhere to live -- the room logged "does not carry crackable stuff data" on every click while
/// the database said format 7 and the inventory honoured it.
/// </summary>
public sealed class FurnitureLogicStuffDataTypeTests
{
    [Theory]
    [InlineData(StuffDataType.CrackableKey, typeof(ICrackableStuffData))]
    [InlineData(StuffDataType.LegacyKey, typeof(ILegacyStuffData))]
    public void TheLogicTakesItsStuffDataFormatFromTheDefinition(
        StuffDataType definitionType,
        System.Type expected
    )
    {
        FurnitureFloorLogic logic = new(new StuffDataFactory(), StubContext(definitionType));

        logic.StuffData.Should().BeAssignableTo(expected);
    }

    private static IRoomFloorItemContext StubContext(StuffDataType stuffDataType)
    {
        FurnitureDefinitionSnapshot definition = new()
        {
            Id = 21411050,
            SpriteId = 21411050,
            Name = "wonderland_c25_crackableb",
            ProductType = ProductType.Floor,
            FurniCategory = FurnitureCategory.Default,
            LogicName = "furniture_crackable",
            TotalStates = 9,
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

        // A freshly placed crackable: no STUFF section yet, so the factory builds a default
        // instance of whatever format the definition asks for -- which is the thing under test.
        IExtraData extraData = new ExtraData(null);

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

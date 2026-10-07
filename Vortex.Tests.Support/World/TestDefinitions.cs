using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Tests.Support;

/// <summary>Furniture definitions with every required member at a neutral value.</summary>
public static class TestDefinitions
{
    public static FurnitureDefinitionSnapshot Floor(int id, string name, int width, int length) =>
        Make(id, name, ProductType.Floor, width, length);

    public static FurnitureDefinitionSnapshot Wall(int id, string name) =>
        Make(id, name, ProductType.Wall, 1, 1);

    private static FurnitureDefinitionSnapshot Make(
        int id,
        string name,
        ProductType type,
        int width,
        int length
    ) =>
        new()
        {
            Id = id,
            SpriteId = id,
            Name = name,
            ProductType = type,
            FurniCategory = FurnitureCategory.Default,
            LogicName = "furniture_basic",
            TotalStates = 2,
            Width = width,
            Length = length,
            StackHeight = 1,
            CanStack = false,
            CanWalk = false,
            CanSit = false,
            CanLay = false,
            CanRecycle = false,
            CanTrade = false,
            CanGroup = false,
            CanSell = false,
            UsagePolicy = FurnitureUsageType.Nobody,
            ExtraData = null,
            StuffDataType = StuffDataType.LegacyKey,
        };
}

using System;
using System.Threading.Tasks;
using Vortex.Primitives;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Object.Logic.Furniture;
using Vortex.Tests.Support;

namespace Vortex.Rooms.Tests.Support;

/// <summary>
/// Floor items for tests that go through the room's real attach and placement code.
/// </summary>
/// <remarks>
/// The coordinates have to be live rather than constant: what these suites are about is which
/// coordinates the map reads and when, and a fake answering a fixed X and Y agrees with any order
/// the room happens to apply.
/// </remarks>
internal static class TestFloorItems
{
    /// <summary>
    /// An item that records the position, rotation and logic the room gives it.
    /// </summary>
    /// <param name="preAttachedLogic">
    /// A logic already on the item before it reaches the room. Attaching refuses these -- an item is
    /// only attached once -- which is how a test reaches the non-throwing failure exit.
    /// </param>
    public static IRoomFloorItem Movable(
        RoomObjectId objectId,
        PlayerId ownerId,
        int width = 1,
        int length = 1,
        IRoomObjectLogic? preAttachedLogic = null,
        bool canWalk = true
    )
    {
        int x = 0;
        int y = 0;
        Rotation rotation = Rotation.North;
        Altitude z = Altitude.Zero;
        IRoomObjectLogic? attached = preAttachedLogic;
        FurnitureDefinitionSnapshot definition = Definition(width, length, canWalk);

        return FakeProxy.Create<IRoomFloorItem>(call =>
        {
            switch (call.Method.Name)
            {
                case nameof(IRoomFloorItem.SetPosition):
                    x = (int)call.Args![0]!;
                    y = (int)call.Args![1]!;

                    return null;
                case nameof(IRoomFloorItem.SetPositionZ):
                    z = (Altitude)call.Args![0]!;

                    return null;
                case nameof(IRoomFloorItem.SetRotation):
                    rotation = (Rotation)call.Args![0]!;

                    return null;
                case nameof(IRoomFloorItem.SetLogic):
                    attached = (IRoomObjectLogic)call.Args![0]!;

                    return null;
                default:
                    return call.Method.Name switch
                    {
                        $"get_{nameof(IRoomFloorItem.ObjectId)}" => objectId,
                        $"get_{nameof(IRoomFloorItem.OwnerId)}" => ownerId,
                        $"get_{nameof(IRoomFloorItem.X)}" => x,
                        $"get_{nameof(IRoomFloorItem.Y)}" => y,
                        $"get_{nameof(IRoomFloorItem.Z)}" => z,
                        $"get_{nameof(IRoomFloorItem.Height)}" => z,
                        $"get_{nameof(IRoomFloorItem.Rotation)}" => rotation,
                        $"get_{nameof(IRoomFloorItem.Logic)}" => attached,
                        $"get_{nameof(IRoomFloorItem.Definition)}" => definition,
                        _ => null,
                    };
            }
        });
    }

    /// <summary>What the logic provider hands back on a healthy attach.</summary>
    public static IFurnitureFloorLogic WorkingLogic() =>
        FakeProxy.Create<IFurnitureFloorLogic>(call =>
            call.Method.Name switch
            {
                nameof(IFurnitureFloorLogic.CanWalk) => true,
                nameof(IFurnitureFloorLogic.CanStack) => true,
                nameof(IFurnitureFloorLogic.CanSit) => false,
                nameof(IFurnitureFloorLogic.CanLay) => false,
                nameof(IFurnitureFloorLogic.GetPostureOffset) => Altitude.Zero,
                _ => null,
            }
        );

    /// <summary>A logic whose own attach hook throws -- behaviour code failing after the room has
    /// already published the item and indexed the logic.</summary>
    public static IFurnitureFloorLogic LogicThatFailsToAttach() =>
        FakeProxy.Create<IFurnitureFloorLogic>(call =>
            call.Method.Name switch
            {
                nameof(IFurnitureFloorLogic.OnAttachAsync) => Task.FromException(
                    new InvalidOperationException("the logic refused to attach")
                ),
                nameof(IFurnitureFloorLogic.CanWalk) => true,
                nameof(IFurnitureFloorLogic.CanStack) => true,
                nameof(IFurnitureFloorLogic.CanSit) => false,
                nameof(IFurnitureFloorLogic.CanLay) => false,
                nameof(IFurnitureFloorLogic.GetPostureOffset) => Altitude.Zero,
                _ => null,
            }
        );

    public static FurnitureDefinitionSnapshot Definition(
        int width,
        int length,
        bool canWalk = true
    ) =>
        new()
        {
            Id = 1,
            SpriteId = 1,
            Name = "test_item",
            ProductType = ProductType.Floor,
            FurniCategory = FurnitureCategory.Default,
            LogicName = "default",
            TotalStates = 1,
            Width = width,
            Length = length,
            StackHeight = Altitude.FromInt(1),
            CanStack = true,
            CanWalk = canWalk,
            CanSit = false,
            CanLay = false,
            CanRecycle = false,
            CanTrade = true,
            CanGroup = false,
            CanSell = true,
            UsagePolicy = FurnitureUsageType.Everybody,
            ExtraData = null,
            StuffDataType = StuffDataType.LegacyKey,
        };
}

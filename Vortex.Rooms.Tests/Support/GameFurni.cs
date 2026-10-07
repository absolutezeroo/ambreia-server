using System;
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
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Tests.Support;

namespace Vortex.Rooms.Tests.Support;

/// <summary>
/// Places a real logic component on a real tile of the harness room: the item lands in
/// <c>ItemsById</c>, on the tile's floor stack and in the item index, which is exactly the three
/// places <c>RoomObjectModule</c> puts it in production.
/// <para>
/// A test that hand-fed a component to a module would never exercise how the runtime finds the
/// furniture through the item index and the map. Nothing uses this helper since the games were
/// removed in the cut of 2026-10-07.
/// </para>
/// </summary>
internal static class GameFurni
{
    private static int _nextObjectId = 5_000;

    public static TComponent Place<TComponent>(
        RoomHarness harness,
        string logicName,
        int x,
        int y,
        Func<StuffDataFactory, IRoomFloorItemContext, TComponent> create,
        string? classname = null,
        Rotation rotation = Rotation.North
    )
        where TComponent : class, IRoomObjectLogic
    {
        RoomObjectId objectId = new(_nextObjectId++);

        FurnitureDefinitionSnapshot definition = new()
        {
            Id = 1,
            SpriteId = 1,
            Name = classname ?? logicName,
            ProductType = ProductType.Floor,
            FurniCategory = FurnitureCategory.Default,
            LogicName = logicName,
            TotalStates = 100,
            Width = 1,
            Length = 1,
            StackHeight = default,
            CanStack = false,
            CanWalk = true,
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

        IExtraData extraData = new ExtraData(null);
        object? logicRef = null;

        // The position is mutable, because the room can move furniture through the map module, and
        // a fake with a fixed X would make every movement assertion pass by accident.
        int[] position = [x, y];

        IRoomFloorItem item = FakeProxy.Create<IRoomFloorItem>(call =>
        {
            if (call.Method.Name == "SetPosition")
            {
                position[0] = (int)call.Args![0]!;
                position[1] = (int)call.Args![1]!;

                return null;
            }

            return call.Method.Name switch
            {
                "get_ExtraData" => extraData,
                "get_Definition" => definition,
                "get_Logic" => logicRef,
                "get_ObjectId" => objectId,
                "get_X" => position[0],
                "get_Y" => position[1],
                "get_Z" => default(Altitude),
                "get_Height" => default(Altitude),
                "get_Rotation" => rotation,
                _ => null,
            };
        });

        IRoomFloorItemContext ctx = FakeProxy.Create<IRoomFloorItemContext>(call =>
            call.Method.Name switch
            {
                "get_Definition" => definition,
                "get_RoomObject" => item,
                "get_ObjectId" => objectId,
                "get_RoomId" => harness.Grain.RoomId,
                // The room itself, through the narrow contracts a component may use.
                "get_Game" => harness.Grain,
                "get_Map" => harness.Grain,
                "get_Lookup" => harness.Grain,
                _ => null,
            }
        );

        TComponent logic = create(new StuffDataFactory(), ctx);
        logicRef = logic;

        harness.Grain._state.ItemsById[objectId] = item;

        int tileIdx = harness.Grain.MapModule.ToIdx(x, y);

        if (harness.Grain.MapModule.InBounds(tileIdx))
        {
            harness.Grain._state.TileFloorStacks[tileIdx].Add(objectId);
        }

        harness.Grain._state.ItemIndex.OnLogicAttached(item);

        return logic;
    }

    /// <summary>Takes a component back out again — the pickup path.</summary>
    public static void Remove(RoomHarness harness, IRoomItem item, int x, int y)
    {
        harness.Grain._state.ItemsById.Remove(item.ObjectId);

        int tileIdx = harness.Grain.MapModule.ToIdx(x, y);

        if (harness.Grain.MapModule.InBounds(tileIdx))
        {
            harness.Grain._state.TileFloorStacks[tileIdx].Remove(item.ObjectId);
        }

        harness.Grain._state.ItemIndex.OnItemDetached(item);
    }
}

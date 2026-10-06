using System.Linq;
using FluentAssertions;
using Vortex.Furniture;
using Vortex.Furniture.Providers;
using Vortex.Primitives.Furniture;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Enums.Games;
using Vortex.Primitives.Rooms.Games.Components;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Rooms.Grains;
using Vortex.Rooms.Object.Logic.Furniture.Floor;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Games;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Games;

/// <summary>
/// The items-by-logic-type index that replaced the per-game "walk all of ItemsById and
/// pattern-match" scans. Its contract: an item is queryable under its logic's concrete type AND
/// every base class (so a family query sees all its members), it disappears on detach, and a
/// never-attached logic is a no-op both ways. These are the properties the game systems now lean on
/// every tick — the game-timer scan alone used to walk the whole room 20 times a second.
/// </summary>
public sealed class RoomItemIndexTests
{
    [Fact]
    public void AttachedItem_IsFindableByConcreteAndBaseType()
    {
        RoomItemIndex index = new();
        IRoomItem gate = BuildItem<TestOnlyArenaTile>("test_tile");

        index.OnLogicAttached(gate);

        index.ItemsOf<TestOnlyArenaTile>().Should().ContainSingle().Which.Should().Be(gate);
        index
            .ItemsOf<FurnitureFloorLogic>()
            .Should()
            .ContainSingle("a family-level query must see derived logics")
            .Which.Should()
            .Be(gate);
    }

    [Fact]
    public void LogicsOf_ReturnsTheTypedLogics_AndOnlyTheRequestedFamily()
    {
        RoomItemIndex index = new();
        IRoomItem gate = BuildItem<TestOnlyArenaTile>("test_tile");
        IRoomItem counter = BuildItem<FurnitureScoreboardLogic>("furniture_score");

        index.OnLogicAttached(gate);
        index.OnLogicAttached(counter);

        index.LogicsOf<TestOnlyArenaTile>().Should().ContainSingle();
        index.LogicsOf<FurnitureScoreboardLogic>().Should().ContainSingle();
        index.LogicsOf<FurnitureFloorLogic>().Should().HaveCount(2);
    }

    [Fact]
    public void AttachedItem_IsFindableByTheCapabilityInterfacesItImplements()
    {
        RoomItemIndex index = new();
        IRoomItem gate = BuildItem<TestOnlyArenaTile>("test_tile");

        index.OnLogicAttached(gate);

        // This is what capability-based game furniture asks for: "the arena tiles in this room",
        // whichever class happens to provide the role. Without interface buckets a game would be
        // back to naming concrete classes, which is naming games in the core by another route.
        index.ItemsOf<IArenaTileComponent>().Should().ContainSingle().Which.Should().Be(gate);
        index.CountOf<IArenaTileComponent>().Should().Be(1);
        index.LogicsOf<IGameComponent>().Should().ContainSingle();
    }

    [Fact]
    public void DetachedItem_DisappearsFromEveryBucket()
    {
        RoomItemIndex index = new();
        IRoomItem gate = BuildItem<TestOnlyArenaTile>("test_tile");

        index.OnLogicAttached(gate);
        index.OnItemDetached(gate);

        index.ItemsOf<TestOnlyArenaTile>().Should().BeEmpty();
        index.ItemsOf<FurnitureFloorLogic>().Should().BeEmpty();
        index.ItemsOf<IArenaTileComponent>().Should().BeEmpty();
    }

    [Fact]
    public void AnItemWhoseLogicNeverAttached_IsANoOpBothWays()
    {
        RoomItemIndex index = new();
        IRoomItem bare = FakeProxy.Create<IRoomItem>(_ => null);

        index.OnLogicAttached(bare);
        index.OnItemDetached(bare);

        index.ItemsOf<FurnitureFloorLogic>().Should().BeEmpty();
    }

    [Fact]
    public void DetachingOneItem_LeavesItsSiblingsIndexed()
    {
        RoomItemIndex index = new();
        IRoomItem first = BuildItem<TestOnlyArenaTile>("test_tile");
        IRoomItem second = BuildItem<TestOnlyArenaTile>("test_tile");

        index.OnLogicAttached(first);
        index.OnLogicAttached(second);
        index.OnItemDetached(first);

        index.ItemsOf<TestOnlyArenaTile>().Should().ContainSingle().Which.Should().Be(second);
    }

    /// <summary>A real logic of type <typeparamref name="TLogic"/> attached to a stub item, built the
    /// way the furniture tests build theirs: FakeProxy context + a real definition snapshot.</summary>
    private static IRoomItem BuildItem<TLogic>(string logicName)
        where TLogic : class, IRoomObjectLogic
    {
        FurnitureDefinitionSnapshot definition = new()
        {
            Id = 1,
            SpriteId = 1,
            Name = logicName,
            ProductType = ProductType.Floor,
            FurniCategory = FurnitureCategory.Default,
            LogicName = logicName,
            TotalStates = 1,
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
        IRoomObjectLogic? logic = null;

        IRoomFloorItem item = FakeProxy.Create<IRoomFloorItem>(call =>
            call.Method.Name switch
            {
                "get_ExtraData" => extraData,
                "get_Definition" => definition,
                "get_Logic" => logic,
                _ => null,
            }
        );

        IRoomFloorItemContext ctx = FakeProxy.Create<IRoomFloorItemContext>(call =>
            call.Method.Name switch
            {
                "get_Definition" => definition,
                "get_RoomObject" => item,
                _ => null,
            }
        );

        logic = (IRoomObjectLogic)
            System.Activator.CreateInstance(typeof(TLogic), new StuffDataFactory(), ctx)!;

        return item;
    }
}

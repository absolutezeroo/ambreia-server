using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Furniture.Providers;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Wall;
using Vortex.Primitives.Rooms.Object.Logic.Furniture;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>A zone's room takes its map and its decor from the published document.</summary>
public sealed class ZoneRoomTests
{
    [Fact]
    public async Task AZone_TakesItsMapFromTheFloor()
    {
        ZoneDocument doc = ZoneSamples.Minimal();
        doc.Floor.Tiles[4 * 6 + 4] = new ZoneTile { Height = 3 };

        RoomHarness harness = await RoomHarness.CreateZoneAsync(doc);

        harness.Grain._state.Model!.Width.Should().Be(6);
        harness.Grain._state.Model.BaseHeights[4 * 6 + 4].Value.Should().Be(3);
        harness.Grain._state.Model.DoorX.Should().Be(0);
        harness.Grain._state.Model.DoorY.Should().Be(2);
    }

    [Fact]
    public async Task ANonWalkableTile_IsClosed()
    {
        ZoneDocument doc = ZoneSamples.Minimal();
        doc.Floor.Tiles[1 * 6 + 1] = new ZoneTile { Walkable = false };

        RoomHarness harness = await RoomHarness.CreateZoneAsync(doc);

        harness.Grain._state.Model!.BaseFlags[1 * 6 + 1].Should().HaveFlag(RoomTileFlags.Closed);
        harness.Grain._state.Model.BaseFlags[1 * 6 + 2].Should().HaveFlag(RoomTileFlags.Open);
    }

    [Fact]
    public async Task AZone_LoadsItsDecor_WithoutReadingFurnitureRows()
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(ZoneSamples.EveryLayer());

        await harness.Grain.FurniModule.EnsureFurniLoadedAsync(default);

        harness
            .Grain._state.ItemsById.Values.Should()
            .ContainSingle()
            .Which.Definition.Id.Should()
            .Be(ZoneSamples.Throne);
        harness.ItemsProviderCalls.Should().Be(0);
    }

    [Fact]
    public async Task ADecorObjectWithAnUnknownDefinition_IsLeftOut()
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(
            ZoneSamples.Minimal() with
            {
                Decor =
                [
                    new ZoneDecorObject
                    {
                        Id = "gone",
                        Definition = 424242,
                        Class = "gone",
                        X = 1,
                        Y = 1,
                    },
                ],
            }
        );

        await harness.Grain.FurniModule.EnsureFurniLoadedAsync(default);

        harness.Grain._state.IsFurniLoaded.Should().BeTrue();
        harness.Grain._state.ItemsById.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ZoneWallSide.Left, ":w=2,0 l=7,31 l")]
    [InlineData(ZoneWallSide.Right, ":w=2,0 l=7,31 r")]
    public async Task AWallObject_KeepsItsSideAndBothOffsets(ZoneWallSide side, string expected)
    {
        ZoneDocument doc = ZoneSamples.Minimal() with
        {
            WallDecor =
            [
                new ZoneWallDecorObject
                {
                    Id = "poster_1",
                    Definition = ZoneSamples.Poster,
                    Class = "poster",
                    Side = side,
                    X = 2,
                    Y = 0,
                    OffsetX = 7,
                    OffsetY = 31,
                },
            ],
        };
        RoomHarness harness = await RoomHarness.CreateZoneAsync(doc);
        harness.LogicFactory = () => FakeProxy.Create<IFurnitureWallLogic>(_ => null);

        await harness.Grain.FurniModule.EnsureFurniLoadedAsync(default);

        harness
            .Grain._state.ItemsById.Values.OfType<IRoomWallItem>()
            .Should()
            .ContainSingle()
            .Which.ConvertWallPositionToString()
            .Should()
            .Be(expected);
    }

    /// <summary>
    /// The state set in the map editor reaches the client: the decor's extra data carries it in its
    /// stuff section, where a furniture logic reads its state. A bare "2" was written there, the
    /// logic found no section, and every decor showed its first state.
    /// </summary>
    [Fact]
    public async Task ADecorsPublishedState_IsTheStateItsLogicReads()
    {
        ZoneDocument doc = ZoneSamples.EveryLayer();
        doc = doc with { Decor = [doc.Decor[0] with { State = 2 }] };
        RoomHarness harness = await RoomHarness.CreateZoneAsync(doc);

        await harness.Grain.FurniModule.EnsureFurniLoadedAsync(default);

        IRoomItem item = harness.Grain._state.ItemsById.Values.Single();
        new StuffDataFactory()
            .CreateStuffDataFromExtraData(item.Definition.StuffDataType, item.ExtraData)
            .GetState()
            .Should()
            .Be(2);
    }

    [Fact]
    public async Task AZone_NeverFlushesItsDecor()
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(ZoneSamples.EveryLayer());
        await harness.Grain.FurniModule.EnsureFurniLoadedAsync(default);
        harness.Grain._state.DirtyItemIds.Add(harness.Grain._state.ItemsById.Keys.Single());

        await harness.FlushItemsAsync();

        harness.PersistenceBatches.Should().BeEmpty();
        harness.Grain._state.DirtyItemIds.Should().BeEmpty();
    }

    [Fact]
    public async Task AnOrdinaryRoom_StillReadsItsFurnitureRows()
    {
        RoomHarness harness = await RoomHarness.CreateAsync();
        harness.LogicFactory = TestFloorItems.WorkingLogic;

        await harness.Grain.ApplyZoneAsync(default);
        await harness.Grain.FurniModule.EnsureFurniLoadedAsync(default);

        harness.Grain._state.Zone.Should().BeNull();
        harness.ItemsProviderCalls.Should().Be(1);
    }
}

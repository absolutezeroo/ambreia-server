using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Events.Zone;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Primitives.Rooms.Object.Logic.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Mapping;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;
using static Vortex.Rooms.Tests.Zones.ZoneVisits;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// Plan D10: a zone furni's <c>interaction</c> decides what using it does, for anyone in the zone
/// -- a door opens and can be walked through, a lever flips, a sign speaks, a teleporter moves.
/// </summary>
public sealed class ZoneInteractionTests
{
    /// <summary>The decor's room object id: the first floor decor is object 1.</summary>
    private static readonly RoomObjectId Decor = new(1);

    /// <summary>A two-tile gate, 2 x 1.</summary>
    private const int Gate = 4242;

    private readonly List<int> _states = [];

    /// <summary>Whether the furni is a seat by its own logic, as a throne is.</summary>
    private bool _seat;

    /// <summary>A solid furni that remembers the states it is set to.</summary>
    private IFurnitureFloorLogic SolidLogic() =>
        FakeProxy.Create<IFurnitureFloorLogic>(call =>
            call.Method.Name switch
            {
                nameof(IFurnitureFloorLogic.CanWalk) => false,
                nameof(IFurnitureFloorLogic.CanStack) => false,
                nameof(IFurnitureFloorLogic.CanSit) => _seat,
                nameof(IFurnitureFloorLogic.CanLay) => false,
                nameof(IFurnitureFloorLogic.GetPostureOffset) => Altitude.Zero,
                nameof(IFurnitureFloorLogic.GetState) => _states.Count > 0 ? _states[^1] : 0,
                nameof(IFurnitureFloorLogic.SetStateAsync) => RecordAsync((int)call.Args![0]!),
                _ => null,
            }
        );

    private Task RecordAsync(int state)
    {
        _states.Add(state);

        return Task.CompletedTask;
    }

    private static ZoneDecorObject Furni(string interaction, string? tag = null) =>
        new()
        {
            Id = "furni",
            Definition = ZoneSamples.Throne,
            Class = "throne",
            X = 3,
            Y = 2,
            Interaction = interaction,
            Tag = tag,
        };

    private async Task<RoomHarness> ZoneWithAsync(ZoneDocument doc)
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(doc);

        harness.LogicFactory = SolidLogic;
        await harness.Grain.FurniModule.EnsureFurniLoadedAsync(default);
        harness.LogicFactory = () => FakeProxy.Create<IRoomPlayerLogic>(_ => null);

        await EnterAsync(harness);

        return harness;
    }

    private Task<RoomHarness> ZoneWithAsync(ZoneDecorObject furni) =>
        ZoneWithAsync(ZoneSamples.Minimal() with { Decor = [furni] });

    /// <summary>A use, then the time to walk up to the furni when it is out of reach.</summary>
    private static async Task<bool> UseAsync(RoomHarness harness)
    {
        bool used = await harness.Grain.UseItemByIdAsync(
            harness.ContextFor(Walker),
            Decor,
            default
        );

        await harness.TickAvatarsAsync(12);

        return used;
    }

    private static bool Walkable(RoomHarness harness, int x, int y) =>
        harness
            .Grain._state.TileFlags[harness.Grain.MapModule.ToIdx(x, y)]
            .Has(RoomTileFlags.Walkable);

    [Fact]
    public async Task ADoor_OpensForAnyone_AndCanBeWalkedThrough()
    {
        RoomHarness harness = await ZoneWithAsync(Furni("door"));
        Walkable(harness, 3, 2).Should().BeFalse();

        (await UseAsync(harness)).Should().BeTrue();

        _states.Should().Equal(1);
        Walkable(harness, 3, 2).Should().BeTrue();

        await WalkAsync(harness, 4, 2);

        (AvatarOf(harness).X, AvatarOf(harness).Y).Should().Be((4, 2));
    }

    [Fact]
    public async Task AClosedDoor_OnASeat_IsNoDestination()
    {
        _seat = true;
        RoomHarness harness = await ZoneWithAsync(Furni("door"));

        await WalkAsync(harness, 3, 2);

        (AvatarOf(harness).X, AvatarOf(harness).Y).Should().NotBe((3, 2));
    }

    [Fact]
    public async Task ADoor_ClosesAgain_ButNotOnSomeone()
    {
        RoomHarness harness = await ZoneWithAsync(Furni("door"));
        await UseAsync(harness);
        await WalkAsync(harness, 3, 2);

        await UseAsync(harness);

        _states.Should().Equal(1);

        await WalkAsync(harness, 1, 2);
        await UseAsync(harness);

        _states.Should().Equal(1, 0);
        Walkable(harness, 3, 2).Should().BeFalse();
    }

    [Fact]
    public async Task AWideDoor_DoesNotCloseOnSomeoneInItsFarHalf()
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(
            ZoneSamples.Minimal() with
            {
                Decor = [Furni("door") with { Definition = Gate, Class = "gate_wide" }],
            }
        );
        harness.Definitions[Gate] = TestDefinitions.Floor(Gate, "gate_wide", 2, 1);
        harness.LogicFactory = SolidLogic;
        await harness.Grain.FurniModule.EnsureFurniLoadedAsync(default);
        harness.LogicFactory = () => FakeProxy.Create<IRoomPlayerLogic>(_ => null);
        await EnterAsync(harness);
        await UseAsync(harness);
        await WalkAsync(harness, 4, 2);

        await UseAsync(harness);

        _states.Should().Equal(1);
    }

    [Fact]
    public async Task ALever_Flips()
    {
        RoomHarness harness = await ZoneWithAsync(Furni("lever"));

        await UseAsync(harness);
        await UseAsync(harness);

        _states.Should().Equal(1, 0);
    }

    [Fact]
    public async Task ASign_ShowsItsText()
    {
        RoomHarness harness = await ZoneWithAsync(Furni("sign", "Bienvenue au village"));

        await UseAsync(harness);

        Sent<NotificationDialogMessageComposer>(harness)
            .Should()
            .ContainSingle()
            .Which.Parameters.Single()
            .Value.Should()
            .Be("Bienvenue au village");
    }

    [Fact]
    public async Task ATeleporter_SendsThePlayerToTheEntryItNames()
    {
        RoomHarness harness = await ZoneWithAsync(
            ZoneSamples.Minimal() with
            {
                Decor = [Furni("teleport", "tower")],
                Entries = [Entry("tower", ZoneEntryKinds.Teleport, 5, 5)],
            }
        );

        await UseAsync(harness);

        (AvatarOf(harness).X, AvatarOf(harness).Y).Should().Be((5, 5));
    }

    [Fact]
    public async Task AFurniOutOfReach_IsWalkedUpToBeforeItIsUsed()
    {
        RoomHarness harness = await ZoneWithAsync(Furni("lever"));

        await harness.Grain.UseItemByIdAsync(harness.ContextFor(Walker), Decor, default);

        _states.Should().BeEmpty("the player stands three tiles away, at the door");

        await harness.TickAvatarsAsync(12);

        _states.Should().Equal(1);
        Math.Max(Math.Abs(AvatarOf(harness).X - 3), Math.Abs(AvatarOf(harness).Y - 2))
            .Should()
            .BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task ATeleporterToAConditionalPassage_KeepsThePassagesRules()
    {
        RoomHarness harness = await ZoneWithAsync(
            ZoneSamples.Minimal() with
            {
                Decor = [Furni("teleport", "road")],
                Passages =
                [
                    new ZonePassage
                    {
                        Id = "road",
                        Area = [new ZoneCell(5, 5)],
                        DestinationZone = "forest",
                        DestinationEntry = "gate",
                        Conditions = [new PlayerLevelCondition(5, 99)],
                    },
                ],
            }
        );
        harness.ZoneRooms["forest"] = new RoomId(77);

        await UseAsync(harness);

        harness.ExpectedArrivals.Should().BeEmpty();
    }

    [Fact]
    public async Task UsingZoneDecor_IsAnEvent_WhateverItDoes()
    {
        RoomHarness harness = await ZoneWithAsync(Furni("sign", "Bonjour"));

        await UseAsync(harness);

        harness
            .RoomEvents.OfType<ZoneDecorUsedEvent>()
            .Should()
            .ContainSingle()
            .Which.DecorId.Should()
            .Be("furni");
    }

    [Fact]
    public async Task ACollisionOverride_MakesASolidFurniWalkable()
    {
        RoomHarness harness = await ZoneWithAsync(
            Furni("") with
            {
                Collision = new ZoneCollision { Walkable = true },
            }
        );

        Walkable(harness, 3, 2).Should().BeTrue();
    }
}

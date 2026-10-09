using System.Linq;
using FluentAssertions;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Geometry;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Rooms.Grains.Zones.Compilation;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// A published document compiled once per activation: what each cell is part of, the entries,
/// the triggers by type, the decor's object ids. The polygon rule is the editor's (edges included).
/// </summary>
public sealed class ZoneCompilerTests
{
    [Fact]
    public void APolygonCoversItsInsideAndItsEdges()
    {
        ZoneGeometry
            .CellsInPolygon([new(1, 1), new(3, 1), new(3, 3), new(1, 3)], 10, 10)
            .Should()
            .HaveCount(9);
        ZoneGeometry
            .CellsInPolygon([new(0, 0), new(2, 0), new(0, 2)], 10, 10)
            .Select(c => (c.X, c.Y))
            .Should()
            .BeEquivalentTo([(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (0, 2)]);
    }

    [Fact]
    public void TheDefaultArrivalIsTheFirstArrivalEntry()
    {
        ZoneDocument doc = ZoneSamples.EveryLayer() with
        {
            Entries =
            [
                new ZoneEntry
                {
                    Id = "back",
                    Kind = "respawn",
                    Cell = new ZoneCell(1, 1),
                },
                new ZoneEntry
                {
                    Id = "gate",
                    Kind = "arrival",
                    Cell = new ZoneCell(2, 2),
                },
                new ZoneEntry
                {
                    Id = "side",
                    Kind = "arrival",
                    Cell = new ZoneCell(3, 3),
                },
            ],
        };

        CompiledZone zone = ZoneCompiler.Compile(doc);

        zone.DefaultArrival!.Id.Should().Be("gate");
        zone.Entries["back"].Cell.Should().Be(new ZoneCell(1, 1));
    }

    [Fact]
    public void OverlappingAreasAreOrderedByPriority_ThenTheLaterOne()
    {
        ZoneDocument doc = ZoneSamples.Minimal() with
        {
            Zones =
            [
                Area("low", 0, [new(0, 0), new(3, 0), new(3, 3), new(0, 3)]),
                Area("high", 5, [new(2, 2), new(4, 2), new(4, 4), new(2, 4)]),
                Area("later", 0, [new(3, 3), new(5, 3), new(5, 5), new(3, 5)]),
            ],
        };

        CompiledZone zone = ZoneCompiler.Compile(doc);

        zone.AreasAt(3, 3).Select(a => a.Id).Should().Equal("high", "later", "low");
        zone.AreasAt(0, 0).Select(a => a.Id).Should().Equal("low");
        zone.AreasAt(5, 0).Should().BeEmpty();
    }

    [Fact]
    public void APassageIsFoundOnEachOfItsCells()
    {
        ZoneDocument doc = ZoneSamples.Minimal() with
        {
            Passages =
            [
                new ZonePassage
                {
                    Id = "to_forest",
                    Area = [new(5, 1), new(5, 2)],
                    DestinationZone = "forest",
                    DestinationEntry = "arrival",
                },
            ],
        };

        CompiledZone zone = ZoneCompiler.Compile(doc);

        zone.PassageAt(5, 2)!.Id.Should().Be("to_forest");
        zone.PassageAt(4, 2).Should().BeNull();
    }

    [Fact]
    public void DecorIdsMapToTheObjectIdsTheRoomGivesThem()
    {
        ZoneDocument doc = ZoneSamples.EveryLayer();

        CompiledZone zone = ZoneCompiler.Compile(doc);

        zone.DecorObjectId("throne_1").Should().Be(1);
        zone.DecorId(1).Should().Be("throne_1");
        zone.DecorObjectId("nope").Should().BeNull();
    }

    /// <summary>Spec F3b §5: what a player can click — the decor with an interaction, and the
    /// decor an Interact trigger listens to (plan F3b, décision 1).</summary>
    [Fact]
    public void TheClickables_AreTheDecorWithAnInteractionOrAnInteractTrigger()
    {
        ZoneDecorObject Decor(string id, string? interaction) =>
            new()
            {
                Id = id,
                Definition = ZoneSamples.Throne,
                Class = "throne",
                X = 1,
                Y = 1,
                Interaction = interaction,
            };
        ZoneDocument doc = ZoneSamples.Minimal() with
        {
            Decor =
            [
                Decor("gate", "door"),
                Decor("vase", null),
                Decor("bell", null),
                Decor("board", "sign"),
            ],
            Triggers =
            [
                new ZoneTrigger
                {
                    Id = "ring",
                    Type = ZoneTriggerType.Interact,
                    Target = "bell",
                    Actions = [new ShowDialogueAction("Ding")],
                },
            ],
        };

        ZoneCompiler
            .Compile(doc)
            .Interactives.Select(c => (c.ObjectId, c.Interaction))
            .Should()
            .Equal((1, "door"), (3, "interact"), (4, "sign"));
    }

    [Fact]
    public void AZoneWithoutInteractions_HasNoClickables() =>
        ZoneCompiler.Compile(ZoneSamples.Minimal()).Interactives.Should().BeEmpty();

    [Fact]
    public void TriggersAreIndexedByWhatTheyListenTo()
    {
        CompiledZone zone = ZoneCompiler.Compile(ZoneSamples.EveryLayer());

        zone.TriggersFor(ZoneTriggerType.EnterZone, "z_safe").Should().ContainSingle();
        zone.TriggersFor(ZoneTriggerType.EnterZone, "elsewhere").Should().BeEmpty();
        zone.Timers.Should().BeEmpty();
    }

    [Fact]
    public void AnInactiveTriggerOrABadPeriod_IsNotIndexed()
    {
        CompiledZone zone = ZoneCompiler.Compile(
            ZoneSamples.EveryLayer() with
            {
                Triggers =
                [
                    new ZoneTrigger
                    {
                        Id = "off",
                        Type = ZoneTriggerType.EnterZone,
                        Target = "z_safe",
                        Active = false,
                    },
                    new ZoneTrigger
                    {
                        Id = "bell",
                        Type = ZoneTriggerType.Timer,
                        Target = "soon",
                    },
                    new ZoneTrigger
                    {
                        Id = "clock",
                        Type = ZoneTriggerType.Timer,
                        Target = "30",
                    },
                ],
            }
        );

        zone.TriggersFor(ZoneTriggerType.EnterZone, "z_safe").Should().BeEmpty();
        zone.Timers.Should().ContainSingle().Which.Seconds.Should().Be(30);
    }

    private static ZoneArea Area(string id, int priority, ZoneCell[] polygon) =>
        new()
        {
            Id = id,
            Kind = "Safe",
            Priority = priority,
            Polygon = [.. polygon],
        };
}

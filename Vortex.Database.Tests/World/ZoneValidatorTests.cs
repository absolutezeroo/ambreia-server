using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Tests.Support;
using Vortex.World;
using Xunit;

namespace Vortex.Database.Tests.World;

/// <summary>One test per rule of spec §5.2: a valid sample plus a single fault.</summary>
public sealed class ZoneValidatorTests
{
    private const int Sofa = 28;

    private static readonly HashSet<string> Known = ["village", "forest"];

    [Fact]
    public void TheMinimalSample_IsValid() => Errors(ZoneSamples.Minimal()).Should().BeEmpty();

    [Fact]
    public void TheFullSample_HasOnlyDeferredReferenceWarnings()
    {
        IReadOnlyList<ZoneFinding> findings = Validate(ZoneSamples.EveryLayer());

        findings.Where(f => f.Severity == ZoneSeverity.Error).Should().BeEmpty();
        findings.Select(f => f.Code).Distinct().Should().BeEquivalentTo(["UnresolvedReference"]);
    }

    [Fact]
    public void AFloorAbove64_IsAnError() =>
        Errors(ZoneSamples.Minimal() with { Floor = Floor(65, 1) })
            .Should()
            .Contain("FloorTooLarge");

    [Fact]
    public void ATileCountThatDoesNotMatchTheSize_IsAnError() =>
        Errors(
                ZoneSamples.Minimal() with
                {
                    Floor = ZoneSamples.Minimal().Floor with { Width = 5 },
                }
            )
            .Should()
            .Contain("FloorTileCount");

    [Fact]
    public void ADoorOnAHole_IsAnError()
    {
        ZoneDocument doc = ZoneSamples.Minimal();
        doc.Floor.Tiles[2 * 6 + 0] = new ZoneTile { Hole = true };

        Errors(doc).Should().Contain("DoorNotWalkable");
    }

    [Fact]
    public void DuplicateIds_AreAnError() =>
        Errors(
                ZoneSamples.EveryLayer() with
                {
                    Npcs =
                    [
                        new ZoneNpc
                        {
                            Id = "throne_1",
                            Template = "x",
                            Cell = new ZoneCell(1, 1),
                        },
                    ],
                }
            )
            .Should()
            .Contain("DuplicateId");

    [Fact]
    public void AnUnknownFurnitureDefinition_IsAnError() =>
        Errors(ZoneSamples.Minimal() with { Decor = [Decor(999, "nope", 1, 1)] })
            .Should()
            .Contain("UnknownFurniture");

    [Fact]
    public void ARotationTheFurnitureCannotTake_IsAnError() =>
        Errors(
                ZoneSamples.Minimal() with
                {
                    Decor = [Decor(ZoneSamples.Throne, "throne", 1, 1) with { Rotation = 3 }],
                }
            )
            .Should()
            .Contain("BadRotation");

    [Fact]
    public void AFootprintOverTheEdge_IsAnError() =>
        Errors(ZoneSamples.Minimal() with { Decor = [Decor(Sofa, "sofa_silo", 5, 0)] })
            .Should()
            .Contain("OutOfFloor");

    [Fact]
    public void ATriggerAimingAtAMissingZone_IsAnError() =>
        Errors(
                ZoneSamples.EveryLayer() with
                {
                    Triggers =
                    [
                        new ZoneTrigger
                        {
                            Id = "t",
                            Type = ZoneTriggerType.EnterZone,
                            Target = "z_nowhere",
                        },
                    ],
                }
            )
            .Should()
            .Contain("BrokenReference");

    [Fact]
    public void APassageToAnUnknownZone_IsAnError() =>
        Errors(
                ZoneSamples.Minimal() with
                {
                    Passages =
                    [
                        new ZonePassage
                        {
                            Id = "p",
                            Area = [new(5, 5)],
                            DestinationZone = "moon",
                            DestinationEntry = "arrival",
                        },
                    ],
                }
            )
            .Should()
            .Contain("UnknownZone");

    [Fact]
    public void ASpawnWalledOffFromTheDoor_IsAWarning()
    {
        ZoneDocument doc = ZoneSamples.EveryLayer();

        // Holes down column 3 cut (4..5, *) off from the door at (0, 2).
        for (int y = 0; y < 6; y++)
        {
            doc.Floor.Tiles[y * 6 + 3] = new ZoneTile { Hole = true };
        }

        Validate(doc)
            .Should()
            .Contain(f =>
                f.Code == "Unreachable"
                && f.ElementId == "spawn_1"
                && f.Severity == ZoneSeverity.Warning
            );
    }

    [Fact]
    public void MoreDecorThanTheBudget_IsAWarning() =>
        Validate(
                ZoneSamples.Minimal() with
                {
                    Decor =
                    [
                        .. Enumerable
                            .Range(0, 4)
                            .Select(i =>
                                Decor(ZoneSamples.Throne, "throne", i, 0) with
                                {
                                    Id = $"d{i}",
                                }
                            ),
                    ],
                }
            )
            .Should()
            .Contain(f => f.Code == "OverBudget" && f.Severity == ZoneSeverity.Warning);

    private static IReadOnlyList<ZoneFinding> Validate(ZoneDocument doc) =>
        new ZoneValidator(
            Definitions(),
            Options.Create(new WorldConfig { DecorBudget = 3 })
        ).Validate(doc, Known);

    private static string[] Errors(ZoneDocument doc) =>
        [.. Validate(doc).Where(f => f.Severity == ZoneSeverity.Error).Select(f => f.Code)];

    private static ZoneDecorObject Decor(int definition, string cls, int x, int y) =>
        new()
        {
            Id = "d",
            Definition = definition,
            Class = cls,
            X = x,
            Y = y,
        };

    private static ZoneFloor Floor(int width, int length) =>
        new()
        {
            Width = width,
            Length = length,
            Tiles = [.. Enumerable.Range(0, width * length).Select(_ => new ZoneTile())],
            Door = new ZoneCell(0, 0),
        };

    private static IFurnitureDefinitionProvider Definitions()
    {
        Dictionary<int, FurnitureDefinitionSnapshot> defs = new()
        {
            [ZoneSamples.Throne] = TestDefinitions.Floor(ZoneSamples.Throne, "throne", 1, 1),
            [Sofa] = TestDefinitions.Floor(Sofa, "sofa_silo", 2, 1),
        };

        return FakeProxy.Create<IFurnitureDefinitionProvider>(call =>
            call.Method.Name == nameof(IFurnitureDefinitionProvider.TryGetDefinition)
                ? defs.GetValueOrDefault((int)call.Args![0]!)
                : null
        );
    }
}

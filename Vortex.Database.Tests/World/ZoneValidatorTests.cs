using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Rpg.Content;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;
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
    public void TheFullSample_HasOnlyDeferredAndInertWarnings()
    {
        IReadOnlyList<ZoneFinding> findings = Validate(ZoneSamples.EveryLayer());

        findings.Where(f => f.Severity == ZoneSeverity.Error).Should().BeEmpty();
        findings
            .Select(f => f.Code)
            .Distinct()
            .Should()
            .BeEquivalentTo(["UnresolvedReference", "InertUntilRpg"]);
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
    public void AWallObjectOffTheFloor_IsAnError() =>
        Errors(
                ZoneSamples.Minimal() with
                {
                    WallDecor =
                    [
                        new ZoneWallDecorObject
                        {
                            Id = "w",
                            Definition = ZoneSamples.Poster,
                            Class = "poster",
                            Side = ZoneWallSide.Left,
                            X = 9,
                            Y = 0,
                        },
                    ],
                }
            )
            .Should()
            .Contain("OutOfFloor");

    [Fact]
    public void AnEntryOffTheFloor_IsAnError() =>
        Errors(
                ZoneSamples.Minimal() with
                {
                    Entries =
                    [
                        new ZoneEntry
                        {
                            Id = "e",
                            Kind = "arrival",
                            Cell = new ZoneCell(7, 7),
                        },
                    ],
                }
            )
            .Should()
            .Contain("OutOfFloor");

    [Fact]
    public void AnAreaOfTwoPoints_IsAnError() =>
        Errors(
                ZoneSamples.Minimal() with
                {
                    Zones =
                    [
                        new ZoneArea
                        {
                            Id = "z",
                            Kind = "Safe",
                            Polygon = [new(0, 0), new(1, 1)],
                        },
                    ],
                }
            )
            .Should()
            .Contain("BadPolygon");

    [Fact]
    public void AFootprintThatOverflows_IsStillOutOfFloor() =>
        Errors(ZoneSamples.Minimal() with { Decor = [Decor(Sofa, "sofa_silo", int.MaxValue, 0)] })
            .Should()
            .Contain("OutOfFloor");

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

    private static IReadOnlyList<ZoneFinding> Validate(
        ZoneDocument doc,
        params MonsterSummary[] monsters
    ) =>
        new ZoneValidator(
            Definitions(),
            Options.Create(new WorldConfig { DecorBudget = 3 }),
            FakeProxy.Create<IRpgContentService>(call =>
                call.Method.Name == nameof(IRpgContentService.GetMonsters)
                    ? (IReadOnlyList<MonsterSummary>)monsters
                    : null
            )
        ).Validate(doc, Known);

    private static MonsterSummary Monster(string id, bool retired = false) =>
        new(id, "normal", "melee", true, "38 0 FFFFFF", retired);

    private static ZoneDocument WithSpawn(ZoneSpawn spawn) =>
        ZoneSamples.Minimal() with
        {
            Spawns = [spawn],
        };

    private static readonly ZoneSpawn Wolves = new()
    {
        Id = "spawn_1",
        Area = [new(4, 4)],
        Monster = "monster.forest.wolf",
    };

    [Fact]
    public void AKnownMonster_HasNoFinding() =>
        Validate(WithSpawn(Wolves), Monster("monster.forest.wolf")).Should().BeEmpty();

    [Fact]
    public void AnUnknownMonster_IsAnError() =>
        Validate(WithSpawn(Wolves with { Monster = "rat" }), Monster("monster.forest.wolf"))
            .Should()
            .ContainSingle(f =>
                f.Code == "UnknownMonster"
                && f.Severity == ZoneSeverity.Error
                && f.ElementId == "spawn_1"
            );

    [Fact]
    public void ARetiredMonster_IsAnError() =>
        Validate(WithSpawn(Wolves), Monster("monster.forest.wolf", retired: true))
            .Should()
            .ContainSingle(f => f.Code == "RetiredMonster" && f.Severity == ZoneSeverity.Error);

    [Fact]
    public void WithoutAnyMonsterInTheContent_AnUnknownOneIsOnlyAWarning() =>
        Validate(WithSpawn(Wolves with { Monster = "rat" }))
            .Should()
            .ContainSingle(f =>
                f.Code == "UnresolvedReference" && f.Severity == ZoneSeverity.Warning
            );

    [Theory]
    [InlineData(3, 1, 1, 1, 30)]
    [InlineData(1, 1, 5, 2, 30)]
    [InlineData(-1, 1, 1, 1, 30)]
    [InlineData(1, 1, 1, 1, -5)]
    public void SpawnBoundsThatCannotWork_AreAnError(
        int min,
        int max,
        int levelMin,
        int levelMax,
        int respawnSeconds
    ) =>
        Errors(
                WithSpawn(
                    Wolves with
                    {
                        Min = min,
                        Max = max,
                        LevelMin = levelMin,
                        LevelMax = levelMax,
                        RespawnSeconds = respawnSeconds,
                    }
                )
            )
            .Should()
            .Contain("BadSpawn");

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
            [ZoneSamples.Poster] = TestDefinitions.Wall(ZoneSamples.Poster, "poster"),
        };

        return FakeProxy.Create<IFurnitureDefinitionProvider>(call =>
            call.Method.Name == nameof(IFurnitureDefinitionProvider.TryGetDefinition)
                ? defs.GetValueOrDefault((int)call.Args![0]!)
                : null
        );
    }
}

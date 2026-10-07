using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Tests.Support;
using Vortex.World;
using Xunit;

namespace Vortex.Database.Tests.World;

/// <summary>
/// What the server does with a zone at run time, checked before it is published (plan phase 6):
/// a trigger aims at the kind of thing its type listens to, an action names what exists, a
/// teleporter leads somewhere, RPG content is flagged inert, loops and ambiguous overlaps warn.
/// </summary>
public sealed class ZoneRuntimeRulesTests
{
    private static readonly HashSet<string> Known = ["village", "forest"];

    private static ZoneArea Area(string id, string kind, int x0, int x1, int priority = 0) =>
        new()
        {
            Id = id,
            Kind = kind,
            Polygon = [new(x0, 0), new(x1, 0), new(x1, 5), new(x0, 5)],
            Priority = priority,
        };

    private static ZoneDecorObject Furni(
        string id,
        string? interaction = null,
        string? tag = null
    ) =>
        new()
        {
            Id = id,
            Definition = ZoneSamples.Throne,
            Class = "throne",
            X = 5,
            Y = 5,
            Interaction = interaction,
            Tag = tag,
        };

    private static ZoneTrigger Trigger(
        ZoneTriggerType type,
        string? target,
        params ZoneAction[] actions
    ) =>
        new()
        {
            Id = "t",
            Type = type,
            Target = target,
            Actions = [.. actions],
        };

    private static ZoneDocument Doc(params ZoneTrigger[] triggers) =>
        ZoneSamples.Minimal() with
        {
            Zones = [Area("market", "Town", 3, 4)],
            Decor = [Furni("lever", "lever")],
            Entries =
            [
                new ZoneEntry
                {
                    Id = "tower",
                    Kind = ZoneEntryKinds.Teleport,
                    Cell = new ZoneCell(1, 1),
                },
            ],
            Triggers = [.. triggers],
        };

    [Fact]
    public void ATimerEveryMinute_IsValid() =>
        Validate(Doc(Trigger(ZoneTriggerType.Timer, "60"))).Should().BeEmpty();

    [Fact]
    public void ATimerWithoutAPeriod_IsAnError() =>
        Errors(Doc(Trigger(ZoneTriggerType.Timer, "soon"))).Should().Equal("BadTimer");

    [Fact]
    public void AnEntryTriggerAimedAtAFurni_IsAnError() =>
        Errors(Doc(Trigger(ZoneTriggerType.EnterZone, "lever"))).Should().Equal("WrongTarget");

    [Fact]
    public void AnInteractTriggerAimedAtAnArea_IsAnError() =>
        Errors(Doc(Trigger(ZoneTriggerType.Interact, "market"))).Should().Equal("WrongTarget");

    [Fact]
    public void AnInteractTriggerOnAFurni_IsValid() =>
        Validate(Doc(Trigger(ZoneTriggerType.Interact, "lever"))).Should().BeEmpty();

    [Fact]
    public void AnActionOnAMissingArea_IsAnError() =>
        Errors(Doc(Trigger(ZoneTriggerType.Interact, "lever", new DeactivateZoneAction("nowhere"))))
            .Should()
            .Equal("BrokenReference");

    [Fact]
    public void OpeningAFurniThatIsNoDoor_IsAWarning() =>
        Codes(Doc(Trigger(ZoneTriggerType.Interact, "lever", new OpenDoorAction("lever"))))
            .Should()
            .Equal("NotADoor");

    [Fact]
    public void ATeleportToAMissingEntryHere_IsAnError() =>
        Errors(
                Doc(
                    Trigger(
                        ZoneTriggerType.Interact,
                        "lever",
                        new TeleportAction("village", "nope")
                    )
                )
            )
            .Should()
            .Equal("BrokenReference");

    [Fact]
    public void ATeleportToAnUnknownZone_IsAnError() =>
        Errors(Doc(Trigger(ZoneTriggerType.Interact, "lever", new TeleportAction("moon", "gate"))))
            .Should()
            .Equal("UnknownZone");

    [Fact]
    public void APassageToAMissingEntryHere_IsAnError() =>
        Errors(
                Doc() with
                {
                    Passages =
                    [
                        new ZonePassage
                        {
                            Id = "road",
                            Area = [new ZoneCell(5, 0)],
                            DestinationZone = "village",
                            DestinationEntry = "nope",
                        },
                    ],
                }
            )
            .Should()
            .Equal("BrokenReference");

    [Fact]
    public void ATeleporterWhoseTagNamesNothing_IsAnError() =>
        Errors(Doc() with { Decor = [Furni("tp", "teleport", "nowhere")] })
            .Should()
            .Equal("BrokenReference");

    [Fact]
    public void ATeleporterToAnEntry_IsValid() =>
        Validate(Doc() with { Decor = [Furni("tp", "teleport", "tower")] }).Should().BeEmpty();

    [Fact]
    public void AnUnknownInteraction_IsAnError() =>
        Errors(Doc() with { Decor = [Furni("x", "explode")] }).Should().Equal("UnknownInteraction");

    [Fact]
    public void ASignWithNothingToSay_IsAWarning() =>
        Codes(Doc() with { Decor = [Furni("s", "sign")] }).Should().Equal("EmptySign");

    [Fact]
    public void RpgTriggersConditionsAndActions_AreInertWarnings()
    {
        IReadOnlyList<ZoneFinding> findings = Validate(
            Doc(
                Trigger(ZoneTriggerType.MonsterKilled, null, new StartQuestAction("q1")) with
                {
                    Conditions = [new PlayerLevelCondition(5, 10)],
                }
            )
        );

        findings.Should().OnlyContain(f => f.Severity == ZoneSeverity.Warning);
        findings
            .Select(f => f.Code)
            .Should()
            .Equal("InertUntilRpg", "InertUntilRpg", "InertUntilRpg");
    }

    [Fact]
    public void TwoAreasThatTeleportIntoEachOther_AreALoopWarning()
    {
        ZoneDocument doc = ZoneSamples.Minimal() with
        {
            Zones = [Area("a", "Town", 1, 1), Area("b", "Town", 4, 4)],
            Entries =
            [
                new ZoneEntry
                {
                    Id = "in_a",
                    Kind = ZoneEntryKinds.Teleport,
                    Cell = new ZoneCell(1, 3),
                },
                new ZoneEntry
                {
                    Id = "in_b",
                    Kind = ZoneEntryKinds.Teleport,
                    Cell = new ZoneCell(4, 3),
                },
            ],
            Triggers =
            [
                Trigger(ZoneTriggerType.EnterZone, "a", new TeleportAction("village", "in_b")) with
                {
                    Id = "ta",
                },
                Trigger(ZoneTriggerType.EnterZone, "b", new TeleportAction("village", "in_a")) with
                {
                    Id = "tb",
                },
            ],
        };

        Codes(doc).Should().Contain("TriggerLoop");
    }

    [Fact]
    public void SafeAndPvpOverlappingAtTheSamePriority_IsAWarning() =>
        Codes(
                ZoneSamples.Minimal() with
                {
                    Zones = [Area("inn", "Safe", 0, 3), Area("arena", "PvP", 2, 5)],
                }
            )
            .Should()
            .Equal("AmbiguousOverlap");

    [Fact]
    public void APriorityThatSettlesTheOverlap_IsValid() =>
        Validate(
                ZoneSamples.Minimal() with
                {
                    Zones = [Area("inn", "Safe", 0, 3, 1), Area("arena", "PvP", 2, 5)],
                }
            )
            .Should()
            .BeEmpty();

    private static IReadOnlyList<ZoneFinding> Validate(ZoneDocument doc) =>
        new ZoneValidator(Definitions(), Options.Create(new WorldConfig())).Validate(doc, Known);

    private static string[] Codes(ZoneDocument doc) => [.. Validate(doc).Select(f => f.Code)];

    private static string[] Errors(ZoneDocument doc) =>
        [.. Validate(doc).Where(f => f.Severity == ZoneSeverity.Error).Select(f => f.Code)];

    private static IFurnitureDefinitionProvider Definitions()
    {
        FurnitureDefinitionSnapshot throne = TestDefinitions.Floor(
            ZoneSamples.Throne,
            "throne",
            1,
            1
        );

        return FakeProxy.Create<IFurnitureDefinitionProvider>(call =>
            call.Method.Name == nameof(IFurnitureDefinitionProvider.TryGetDefinition)
            && (int)call.Args![0]! == ZoneSamples.Throne
                ? throne
                : null
        );
    }
}

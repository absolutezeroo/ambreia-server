using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rpg.Content;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;
using Vortex.World;
using Xunit;

namespace Vortex.Database.Tests.World;

/// <summary>
/// Spec Z1 §9: the validator says a rule has no effect when no module of this server declares it,
/// not from a list of its own; and it checks the rules that now run: level bounds, items in the
/// content, the spawn a SpawnGroup wakes, what each new trigger aims at, and the spawns that sleep.
/// </summary>
public sealed class ZoneDeclaredRulesTests
{
    private static readonly HashSet<string> Known = ["village"];

    private static ZoneSpawn Spawn(string id, string? group = null) =>
        new()
        {
            Id = id,
            Area = [new ZoneCell(2, 2)],
            Monster = "monster.forest.wolf",
            Group = group,
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
            Spawns = [Spawn("wolves", "pack")],
            Triggers = [.. triggers],
        };

    private static IReadOnlyList<ZoneFinding> Validate(
        ZoneDocument doc,
        IEnumerable<IZoneRuleDeclaration>? rules = null,
        IEnumerable<IZoneTriggerSource>? triggers = null,
        bool withContent = true
    ) =>
        new ZoneValidator(
            FakeProxy.Create<IFurnitureDefinitionProvider>(_ => null),
            Options.Create(new WorldConfig()),
            withContent
                ? FakeProxy.Create<IRpgContentService>(call =>
                    call.Method.Name switch
                    {
                        nameof(IRpgContentService.GetMonsters) => (IReadOnlyList<MonsterSummary>)
                            [
                                new(
                                    "monster.forest.wolf",
                                    "normal",
                                    "melee",
                                    true,
                                    "38 0 FFFFFF",
                                    false
                                ),
                            ],
                        nameof(IRpgContentService.GetItemIds) => (IReadOnlyList<string>)
                            ["item.key"],
                        nameof(IRpgContentService.GetQuestIds) => (IReadOnlyList<string>)
                            ["quest.camp.wolves"],
                        nameof(IRpgContentService.GetDialogueIds) => (IReadOnlyList<string>)
                            ["dialogue.camp.elder"],
                        _ => null,
                    }
                )
                : null,
            rules ?? ShippedRules.Rules,
            triggers ?? ShippedRules.Triggers
        ).Validate(doc, Known);

    private static string[] Codes(IReadOnlyList<ZoneFinding> findings) =>
        [.. findings.Select(f => f.Code)];

    [Fact]
    public void AConditionNobodyDeclared_IsInert() =>
        Codes(
                Validate(
                    Doc(
                        Trigger(ZoneTriggerType.EnterZone, null) with
                        {
                            Conditions = [new FactionCondition("red")],
                        }
                    ),
                    rules: ShippedRules.Rules
                )
            )
            .Should()
            .Contain("InertUntilRpg");

    [Fact]
    public void ADeclaredCondition_IsNot() =>
        Codes(
                Validate(
                    Doc(
                        Trigger(ZoneTriggerType.Timer, "60") with
                        {
                            Conditions = [new PlayerLevelCondition(2, 0)],
                        }
                    )
                )
            )
            .Should()
            .NotContain("InertUntilRpg");

    [Fact]
    public void AnUndeclaredCondition_WhenNoModuleDeclaresIt_IsInert() =>
        Codes(
                Validate(
                    Doc(
                        Trigger(ZoneTriggerType.Timer, "60") with
                        {
                            Conditions = [new PlayerLevelCondition(2, 0)],
                        }
                    ),
                    rules: [new DeclaredZoneRule(typeof(TimeOfDayCondition))]
                )
            )
            .Should()
            .Contain("InertUntilRpg");

    [Fact]
    public void AMonsterKilledTrigger_OnItsSpawn_IsFine() =>
        Codes(Validate(Doc(Trigger(ZoneTriggerType.MonsterKilled, "wolves")))).Should().BeEmpty();

    [Fact]
    public void AMonsterKilledTrigger_OnTheSpawnsMonster_IsFine() =>
        Codes(Validate(Doc(Trigger(ZoneTriggerType.MonsterKilled, "monster.forest.wolf"))))
            .Should()
            .BeEmpty();

    [Fact]
    public void AMonsterKilledTrigger_OnNothingHere_IsAWrongTarget() =>
        Codes(Validate(Doc(Trigger(ZoneTriggerType.MonsterKilled, "monster.sea.crab"))))
            .Should()
            .Equal("WrongTarget");

    [Theory]
    [InlineData("1")]
    [InlineData("two")]
    public void APlayerLevelTrigger_AimsAtALevelOfTwoOrMore(string target) =>
        Codes(Validate(Doc(Trigger(ZoneTriggerType.PlayerLevel, target))))
            .Should()
            .Equal("WrongTarget");

    [Fact]
    public void AnItemOwnedTrigger_OnAnUnknownItem_IsAnError() =>
        Codes(Validate(Doc(Trigger(ZoneTriggerType.ItemOwned, "item.nothing"))))
            .Should()
            .Equal("UnknownItem");

    [Fact]
    public void AnItemOwnedTrigger_OnAKnownItem_IsFine() =>
        Codes(Validate(Doc(Trigger(ZoneTriggerType.ItemOwned, "item.key")))).Should().BeEmpty();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 3)]
    public void APlayerLevelCondition_OutOfBounds_IsAnError(int min, int max) =>
        Codes(
                Validate(
                    Doc(
                        Trigger(ZoneTriggerType.Timer, "60") with
                        {
                            Conditions = [new PlayerLevelCondition(min, max)],
                        }
                    )
                )
            )
            .Should()
            .Equal("BadLevel");

    [Fact]
    public void AHasItemCondition_OnAnUnknownItem_IsAnError() =>
        Codes(
                Validate(
                    Doc(
                        Trigger(ZoneTriggerType.Timer, "60") with
                        {
                            Conditions = [new HasItemCondition("item.nothing", 1)],
                        }
                    )
                )
            )
            .Should()
            .Equal("UnknownItem");

    [Fact]
    public void AHasItemCondition_WithoutContent_IsAWarning()
    {
        IReadOnlyList<ZoneFinding> findings = Validate(
            ZoneSamples.Minimal() with
            {
                Triggers =
                [
                    Trigger(ZoneTriggerType.Timer, "60") with
                    {
                        Conditions = [new HasItemCondition("item.key", 1)],
                    },
                ],
            },
            withContent: false
        );

        findings.Should().ContainSingle(f => f.Code == "UnresolvedReference");
        findings.Should().OnlyContain(f => f.Severity == ZoneSeverity.Warning);
    }

    [Fact]
    public void ASpawnGroupNamingNothing_IsAnError() =>
        Codes(Validate(Doc(Trigger(ZoneTriggerType.Timer, "60", new SpawnGroupAction("ghosts")))))
            .Should()
            .Equal("BrokenReference");

    [Fact]
    public void ASpawnGroupNamingAGroup_IsFine_AndItsSpawnsAreSaidToSleep() =>
        Codes(Validate(Doc(Trigger(ZoneTriggerType.Timer, "60", new SpawnGroupAction("pack")))))
            .Should()
            .Equal("DormantSpawn");

    private static ZoneNpc Npc(string? dialogue) =>
        new()
        {
            Id = "elder",
            Template = "npc.villager",
            Cell = new ZoneCell(2, 3),
            Dialogue = dialogue,
        };

    [Fact]
    public void AQuestStateCondition_OnAnUnknownQuest_IsAnError() =>
        Codes(
                Validate(
                    Doc(
                        Trigger(ZoneTriggerType.Timer, "60") with
                        {
                            Conditions = [new QuestStateCondition("quest.nothing", "done")],
                        }
                    )
                )
            )
            .Should()
            .Equal("UnknownQuest");

    [Fact]
    public void AStartQuestAction_OnAnUnknownQuest_IsAnError() =>
        Codes(
                Validate(
                    Doc(Trigger(ZoneTriggerType.Timer, "60", new StartQuestAction("quest.nothing")))
                )
            )
            .Should()
            .Equal("UnknownQuest");

    [Fact]
    public void AnEmptyQuest_IsAnUnknownOne() =>
        Codes(Validate(Doc(Trigger(ZoneTriggerType.Timer, "60", new StartQuestAction("")))))
            .Should()
            .Equal("UnknownQuest");

    [Fact]
    public void AQuestStateTrigger_OnAnUnknownQuest_IsAnError() =>
        Codes(Validate(Doc(Trigger(ZoneTriggerType.QuestState, "quest.nothing"))))
            .Should()
            .Equal("UnknownQuest");

    [Fact]
    public void QuestRules_OnAKnownQuest_AreFine() =>
        Codes(
                Validate(
                    Doc(
                        Trigger(
                            ZoneTriggerType.QuestState,
                            "quest.camp.wolves",
                            new StartQuestAction("quest.camp.wolves")
                        ) with
                        {
                            Conditions = [new QuestStateCondition("quest.camp.wolves", "done")],
                        }
                    )
                )
            )
            .Should()
            .BeEmpty();

    [Fact]
    public void AQuestRule_WithoutContent_IsAWarning() =>
        Validate(
                Doc(
                    Trigger(ZoneTriggerType.Timer, "60", new StartQuestAction("quest.camp.wolves"))
                ),
                withContent: false
            )
            .Should()
            .Contain(f =>
                f.Code == "UnresolvedReference"
                && f.Severity == ZoneSeverity.Warning
                && f.Message.Contains("quest")
            );

    [Fact]
    public void AnNpc_WithAnUnknownDialogue_IsAnError() =>
        Validate(ZoneSamples.Minimal() with { Npcs = [Npc("dialogue.nothing")] })
            .Should()
            .ContainSingle(f =>
                f.Code == "UnknownDialogue"
                && f.Severity == ZoneSeverity.Error
                && f.ElementId == "elder"
            );

    [Fact]
    public void AnNpc_WithAKnownDialogue_OrNone_HasNoDialogueFinding() =>
        Codes(
                Validate(
                    ZoneSamples.Minimal() with
                    {
                        Npcs =
                        [
                            Npc("dialogue.camp.elder"),
                            Npc(null) with
                            {
                                Id = "scout",
                                Cell = new ZoneCell(3, 3),
                            },
                        ],
                    }
                )
            )
            .Should()
            .NotContain("UnknownDialogue");
}

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Validation;
using Xunit;

namespace Vortex.Rpg.Tests.Content;

/// <summary>The rules of a dialogue (spec M2a §2.2, §2.3): nodes reached, one action per choice, quests known.</summary>
public sealed class DialogueKindTests
{
    private const string Json = """
        {
          "id": "dialogue.camp.elder",
          "kind": "dialogue",
          "entries": [{"quest": "quest.camp.wolves", "state": "ready", "node": "thanks"}],
          "nodes": {
            "start": {"text": "dialogue.camp.elder.start", "choices": [
              {"text": "dialogue.camp.elder.ask", "next": "offer"},
              {"text": "dialogue.common.bye"}
            ]},
            "offer": {"text": "dialogue.camp.elder.offer", "choices": [{"text": "dialogue.common.accept", "accept": "quest.camp.wolves"}]},
            "thanks": {"text": "dialogue.camp.elder.thanks", "choices": [{"text": "dialogue.common.complete", "complete": "quest.camp.wolves"}]}
          }
        }
        """;

    private static readonly QuestDefinition Wolves = new()
    {
        Id = "quest.camp.wolves",
        Kind = "quest",
        Level = 1,
        Objectives = [],
    };

    private static DialogueDefinition Parse(string json) =>
        (DialogueDefinition)new DialogueKind().Parse(json);

    private static string[] Codes(DialogueDefinition d) =>
        [
            .. new DialogueKind()
                .Check(
                    d,
                    new ContentCheck(
                        new Dictionary<string, ContentDefinition>
                        {
                            [d.Id] = d,
                            [Wolves.Id] = Wolves,
                        },
                        _ => false
                    )
                )
                .Select(f => f.Code),
        ];

    [Fact]
    public void AValidDialogue_HasNoFinding()
    {
        DialogueDefinition d = Parse(Json);

        Codes(d).Should().BeEmpty();
        d.Nodes["start"].Choices[1].Kind.Should().Be("close");
        d.Nodes["offer"].Choices[0].Quest.Should().Be("quest.camp.wolves");
    }

    [Fact]
    public void ANextToNowhere_IsRefused() =>
        Codes(Parse(Json.Replace("\"next\": \"offer\"", "\"next\": \"nowhere\"")))
            .Should()
            .Equal("BrokenNext");

    [Fact]
    public void AChoiceWithTwoActions_IsRefused() =>
        Codes(
                Parse(
                    Json.Replace(
                        "\"accept\": \"quest.camp.wolves\"",
                        "\"accept\": \"quest.camp.wolves\", \"next\": \"start\""
                    )
                )
            )
            .Should()
            .Equal("TwoActions");

    [Fact]
    public void AnUnknownQuest_IsRefused() =>
        Codes(
                Parse(
                    Json.Replace(
                        "\"complete\": \"quest.camp.wolves\"",
                        "\"complete\": \"quest.nope\""
                    )
                )
            )
            .Should()
            .Equal("BrokenReference");

    [Fact]
    public void AnEntryOnABadState_IsRefused() =>
        Codes(Parse(Json.Replace("\"state\": \"ready\"", "\"state\": \"finished\"")))
            .Should()
            .Equal("BadState");

    [Fact]
    public void ADialogueWithoutStart_IsRefused() =>
        Codes(Parse(Json.Replace("\"start\":", "\"begin\":"))).Should().Contain("NoStart");

    [Fact]
    public void EveryText_IsDeclared() =>
        new DialogueKind()
            .TextKeys(Parse(Json))
            .Should()
            .BeEquivalentTo(
                "dialogue.camp.elder.start",
                "dialogue.camp.elder.ask",
                "dialogue.common.bye",
                "dialogue.camp.elder.offer",
                "dialogue.common.accept",
                "dialogue.camp.elder.thanks",
                "dialogue.common.complete"
            );
}

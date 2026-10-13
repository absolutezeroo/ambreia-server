using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Dialogues;
using Xunit;

namespace Vortex.Rpg.Tests.Dialogues;

/// <summary>Walking a dialogue (spec M2a §2.2): the opening node by quest state, the choices a player may see.</summary>
public sealed class DialogueWalkerTests
{
    private static readonly DialogueDefinition Elder = (DialogueDefinition)
        new DialogueKind().Parse(
            """
            {
              "id": "dialogue.camp.elder",
              "kind": "dialogue",
              "entries": [
                {"quest": "quest.camp.wolves", "state": "ready", "node": "thanks"},
                {"quest": "quest.camp.wolves", "state": "active", "node": "waiting"}
              ],
              "nodes": {
                "start": {"text": "t.start", "choices": [{"text": "t.ask", "next": "offer"}, {"text": "t.bye"}]},
                "offer": {"text": "t.offer", "choices": [{"text": "t.accept", "accept": "quest.camp.wolves"}, {"text": "t.later"}]},
                "waiting": {"text": "t.waiting", "choices": [{"text": "t.bye"}]},
                "thanks": {"text": "t.thanks", "choices": [{"text": "t.complete", "complete": "quest.camp.wolves"}, {"text": "t.bye"}]}
              }
            }
            """
        );

    private static System.Func<string, string> States(params (string Quest, string State)[] states)
    {
        Dictionary<string, string> map = states.ToDictionary(s => s.Quest, s => s.State);

        return quest => map.GetValueOrDefault(quest, "none");
    }

    [Fact]
    public void TheOpening_FollowsTheQuestState()
    {
        DialogueWalker.OpeningNode(Elder, States()).Should().Be("start");
        DialogueWalker
            .OpeningNode(Elder, States(("quest.camp.wolves", "active")))
            .Should()
            .Be("waiting");
        DialogueWalker
            .OpeningNode(Elder, States(("quest.camp.wolves", "ready")))
            .Should()
            .Be("thanks");
        DialogueWalker
            .OpeningNode(Elder, States(("quest.camp.wolves", "done")))
            .Should()
            .Be("start");
    }

    [Fact]
    public void AnOfferThatCannotBeTaken_IsNotShown_AndIndexesStayTheContents()
    {
        DialogueNodeView offered = DialogueWalker.View(Elder, "offer", States(), _ => true);
        DialogueNodeView refused = DialogueWalker.View(Elder, "offer", States(), _ => false);

        offered.Choices.Select(c => (c.Index, c.Kind)).Should().Equal((0, "accept"), (1, "close"));
        refused.Choices.Select(c => (c.Index, c.Kind)).Should().Equal((1, "close"));
        offered.Speaker.Should().Be("dialogue.camp.elder.name");
        offered.Text.Should().Be("t.offer");
    }

    [Fact]
    public void AHandInIsShown_OnlyWhenTheQuestIsReady()
    {
        DialogueWalker
            .View(Elder, "thanks", States(("quest.camp.wolves", "active")), _ => true)
            .Choices.Select(c => c.Kind)
            .Should()
            .Equal("close");
        DialogueWalker
            .View(Elder, "thanks", States(("quest.camp.wolves", "ready")), _ => true)
            .Choices.Select(c => c.Kind)
            .Should()
            .Equal("complete", "close");
    }
}

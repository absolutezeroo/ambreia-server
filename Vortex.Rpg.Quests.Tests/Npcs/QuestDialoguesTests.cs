using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Primitives.Rpg.Quests.Grains;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Dialogues;
using Vortex.Rpg.Npcs;
using Vortex.Rpg.Quests.Npcs;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Quests.Tests.Npcs;

/// <summary>The dialogue service (spec M2a §2.2, §3.3): what a node shows, what a choice does, which marker.</summary>
public sealed class QuestDialoguesTests
{
    private static readonly DialogueDefinition Elder = (DialogueDefinition)
        new DialogueKind().Parse(
            """
            {
              "id": "dialogue.camp.elder",
              "kind": "dialogue",
              "entries": [{"quest": "quest.camp.wolves", "state": "ready", "node": "thanks"}],
              "nodes": {
                "start": {"text": "t.start", "choices": [{"text": "t.ask", "next": "offer"}, {"text": "t.bye"}]},
                "offer": {"text": "t.offer", "choices": [{"text": "t.accept", "accept": "quest.camp.wolves"}, {"text": "t.later"}]},
                "thanks": {"text": "t.thanks", "choices": [{"text": "t.complete", "complete": "quest.camp.wolves"}]}
              }
            }
            """
        );

    private static readonly QuestDefinition Wolves = new()
    {
        Id = "quest.camp.wolves",
        Kind = "quest",
        Level = 1,
        Objectives = [],
        Requires = [new PlayerLevelCondition(1, 0)],
    };

    private readonly List<string> _log = [];

    private QuestDialogues Service() =>
        new(
            FakeProxy.Create<IRpgContent>(call =>
                call.Method.Name == nameof(IRpgContent.Get)
                    ? (string)call.Args![0]! switch
                    {
                        "dialogue.camp.elder" => Elder,
                        "quest.camp.wolves" => Wolves,
                        _ => null,
                    }
                    : null
            ),
            FakeProxy.Create<IGrainFactory>(_ =>
                FakeProxy.Create<IQuestLogGrain>(call =>
                {
                    switch (call.Method.Name)
                    {
                        case nameof(IQuestLogGrain.CountAsync):
                            QuestSignal s = (QuestSignal)call.Args![0]!;
                            _log.Add($"signal {s.Name} {s.Zone} {s.Subject["npc"]}");

                            return Task.CompletedTask;
                        case nameof(IQuestLogGrain.AcceptAsync):
                            _log.Add($"accept {call.Args![0]}");

                            return Task.FromResult(QuestCodes.Ok);
                        case nameof(IQuestLogGrain.CompleteAsync):
                            _log.Add($"complete {call.Args![0]}");

                            return Task.FromResult(QuestCodes.Ok);
                        default:
                            return null;
                    }
                })
            )
        );

    private static NpcConversation Talk(
        bool requirementsHold = true,
        params (string Quest, string State)[] states
    ) =>
        new(
            new PlayerId(7),
            "camp",
            "elder",
            "dialogue.camp.elder",
            states.ToDictionary(s => s.Quest, s => s.State),
            _ => requirementsHold
        );

    [Fact]
    public async Task Opening_ShowsTheNode_AndCountsTheTalk()
    {
        DialogueNodeView? view = await Service().OpenAsync(Talk());

        view!.NodeId.Should().Be("start");
        view.Choices.Select(c => c.Kind).Should().Equal("next", "close");
        _log.Should().Equal("signal npc_talked camp elder");
    }

    [Fact]
    public async Task AnUnknownDialogue_OpensNothing() =>
        (await Service().OpenAsync(Talk() with { DialogueId = "dialogue.nope" })).Should().BeNull();

    [Fact]
    public async Task Next_ShowsTheNextNode_AndAccept_TakesTheQuestThenCloses()
    {
        QuestDialogues service = Service();

        DialogueStep next = await service.ChooseAsync(Talk(), "start", 0);
        next.Next!.NodeId.Should().Be("offer");

        DialogueStep accept = await service.ChooseAsync(Talk(), "offer", 0);
        (accept.Next, accept.Action, accept.Code, accept.QuestId)
            .Should()
            .Be((null, "accepted", QuestCodes.Ok, "quest.camp.wolves"));
        _log.Should().Equal("accept quest.camp.wolves");
    }

    [Fact]
    public async Task AnUnmetRequirement_HidesTheOffer()
    {
        QuestDialogues service = Service();

        (await service.ChooseAsync(Talk(requirementsHold: false), "start", 0))
            .Next!.Choices.Select(c => c.Kind)
            .Should()
            .Equal("close");
        (await service.ChooseAsync(Talk(requirementsHold: false), "offer", 0))
            .IsIgnored.Should()
            .BeTrue("a crafted choose of a hidden offer");
        _log.Should().BeEmpty();
    }

    [Fact]
    public async Task HandingIn_CompletesThenCloses()
    {
        DialogueStep step = await Service()
            .ChooseAsync(Talk(true, ("quest.camp.wolves", "ready")), "thanks", 0);

        (step.Action, step.Code).Should().Be(("completed", QuestCodes.Ok));
        _log.Should().Equal("complete quest.camp.wolves");
    }

    [Fact]
    public async Task APlainClose_ClosesWithoutAction()
    {
        DialogueStep step = await Service().ChooseAsync(Talk(), "start", 1);

        (step.IsIgnored, step.Next, step.Action).Should().Be((false, null, null));
    }

    [Fact]
    public void TheMarker_SaysWhatTheNpcHasForThisPlayer()
    {
        QuestDialogues service = Service();

        service.Marker(Talk()).Should().Be("available");
        service.Marker(Talk(requirementsHold: false)).Should().Be("");
        service.Marker(Talk(true, ("quest.camp.wolves", "active"))).Should().Be("active");
        service.Marker(Talk(true, ("quest.camp.wolves", "ready"))).Should().Be("ready");
        service.Marker(Talk(true, ("quest.camp.wolves", "done"))).Should().Be("");
    }
}

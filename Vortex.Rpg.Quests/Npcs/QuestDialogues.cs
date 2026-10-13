using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Protocol.Messages.Outgoing.Rpg.Quests;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Dialogues;
using Vortex.Rpg.Npcs;

namespace Vortex.Rpg.Quests.Npcs;

/// <summary>
/// The dialogues of the content, walked for one player (spec M2a §2.2, §3.3): an offer is shown when
/// the quest is not in the log, not retired and its requirements hold; a hand-in when it is ready. A
/// choice not shown is ignored, whoever sends it.
/// </summary>
public sealed class QuestDialogues(IRpgContent content, IGrainFactory grains) : INpcDialogues
{
    public async Task<DialogueNodeView?> OpenAsync(NpcConversation c)
    {
        if (
            content.Get<DialogueDefinition>(c.DialogueId) is not { } dialogue
            || !dialogue.Nodes.ContainsKey("start")
        )
        {
            return null;
        }

        await grains
            .GetQuestLogGrain(c.Player)
            .CountAsync(
                new QuestSignal(
                    QuestSignals.NpcTalked,
                    c.Zone,
                    ImmutableDictionary<string, string>.Empty.Add("npc", c.NpcId)
                )
            );

        return DialogueWalker.View(
            dialogue,
            DialogueWalker.OpeningNode(dialogue, q => StateOf(c, q)),
            q => StateOf(c, q),
            q => Offerable(c, q)
        );
    }

    public async Task<DialogueStep> ChooseAsync(NpcConversation c, string nodeId, int choiceIndex)
    {
        if (
            content.Get<DialogueDefinition>(c.DialogueId) is not { } dialogue
            || !dialogue.Nodes.TryGetValue(nodeId, out DialogueNodeDefinition? node)
        )
        {
            return DialogueStep.Ignored;
        }

        DialogueNodeView shown = DialogueWalker.View(
            dialogue,
            nodeId,
            q => StateOf(c, q),
            q => Offerable(c, q)
        );

        if (!shown.Choices.Any(v => v.Index == choiceIndex))
        {
            return DialogueStep.Ignored;
        }

        DialogueChoiceDefinition choice = node.Choices[choiceIndex];

        if (choice.Accept is { } take)
        {
            return DialogueStep.Close(
                "accepted",
                await grains.GetQuestLogGrain(c.Player).AcceptAsync(take),
                take
            );
        }

        if (choice.Complete is { } give)
        {
            return DialogueStep.Close(
                "completed",
                await grains.GetQuestLogGrain(c.Player).CompleteAsync(give),
                give
            );
        }

        return choice.Next is { } next
            ? DialogueStep.Show(
                DialogueWalker.View(dialogue, next, q => StateOf(c, q), q => Offerable(c, q))
            )
            : DialogueStep.Close(null, QuestCodes.Ok, string.Empty);
    }

    public string Marker(NpcConversation c)
    {
        if (content.Get<DialogueDefinition>(c.DialogueId) is not { } dialogue)
        {
            return RpgNpcMarker.None;
        }

        List<DialogueChoiceDefinition> choices =
        [
            .. dialogue.Nodes.Values.SelectMany(n => n.Choices),
        ];

        if (choices.Any(ch => ch.Complete is { } q && StateOf(c, q) == "ready"))
        {
            return RpgNpcMarker.Ready;
        }

        if (choices.Any(ch => ch.Accept is { } q && Offerable(c, q)))
        {
            return RpgNpcMarker.Available;
        }

        return choices.Any(ch => ch.Complete is { } q && StateOf(c, q) == "active")
            ? RpgNpcMarker.Active
            : RpgNpcMarker.None;
    }

    private static string StateOf(NpcConversation c, string quest) =>
        c.QuestStates.GetValueOrDefault(quest) ?? QuestCodes.Name(QuestStatus.None);

    private bool Offerable(NpcConversation c, string quest) =>
        StateOf(c, quest) == QuestCodes.Name(QuestStatus.None)
        && content.Get<QuestDefinition>(quest) is { Retired: false } definition
        && c.AllHold(definition.Requires);
}

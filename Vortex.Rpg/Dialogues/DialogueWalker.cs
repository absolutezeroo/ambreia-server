using System;
using System.Collections.Immutable;
using System.Linq;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Dialogues;

/// <summary>
/// Where a dialogue opens and what a node shows (spec M2a §2.2), for one player: the quest states and
/// what is offerable are the caller's (the log, the room's rules). An offer that cannot be taken and
/// a hand-in that is not ready are not shown; indexes stay the content's.
/// </summary>
public static class DialogueWalker
{
    public static string OpeningNode(DialogueDefinition dialogue, Func<string, string> stateOf) =>
        dialogue
            .Entries.FirstOrDefault(e =>
                stateOf(e.Quest) == e.State && dialogue.Nodes.ContainsKey(e.Node)
            )
            ?.Node
        ?? "start";

    public static DialogueNodeView View(
        DialogueDefinition dialogue,
        string nodeId,
        Func<string, string> stateOf,
        Func<string, bool> offerable
    )
    {
        DialogueNodeDefinition node = dialogue.Nodes[nodeId];

        return new DialogueNodeView(
            nodeId,
            $"{dialogue.Id}.name",
            node.Text,
            [
                .. node
                    .Choices.Select((c, i) => (Choice: c, Index: i))
                    .Where(x => x.Choice.Accept is not { } take || offerable(take))
                    .Where(x => x.Choice.Complete is not { } give || stateOf(give) == "ready")
                    .Select(x => new DialogueChoiceView(x.Index, x.Choice.Text, x.Choice.Kind)),
            ]
        );
    }
}

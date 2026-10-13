using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Content.Kinds;

/// <summary>Dialogues (spec M2a §2.2, §2.3): server only; every node reached, every quest known.</summary>
public sealed class DialogueKind : ContentKind<DialogueDefinition>
{
    private static readonly HashSet<string> States = ["none", "active", "ready", "done"];

    public override string Kind => "dialogue";

    protected override IEnumerable<string> TextKeys(DialogueDefinition d) =>
        d.Nodes.Values.SelectMany(n => n.Choices.Select(c => c.Text).Prepend(n.Text)).Distinct();

    protected override IEnumerable<ContentFinding> Check(DialogueDefinition d, ContentCheck check)
    {
        if (!d.Nodes.ContainsKey("start"))
        {
            yield return Error("NoStart", d.Id, "A dialogue opens at a node named \"start\".");
        }

        foreach (DialogueEntryDefinition entry in d.Entries)
        {
            if (!States.Contains(entry.State))
            {
                yield return Error(
                    "BadState",
                    d.Id,
                    $"entries: state '{entry.State}': none, active, ready or done."
                );
            }

            if (!d.Nodes.ContainsKey(entry.Node))
            {
                yield return Error("BrokenNext", d.Id, $"entries: no node '{entry.Node}'.");
            }

            if (!check.Is<QuestDefinition>(entry.Quest))
            {
                yield return Error("BrokenReference", d.Id, $"entries: no quest '{entry.Quest}'.");
            }
        }

        foreach ((string name, DialogueNodeDefinition node) in d.Nodes)
        {
            foreach (DialogueChoiceDefinition c in node.Choices)
            {
                if (new[] { c.Next, c.Accept, c.Complete }.Count(a => a is not null) > 1)
                {
                    yield return Error(
                        "TwoActions",
                        d.Id,
                        $"{name}: a choice does one thing ({c.Text})."
                    );
                }

                if (c.Next is not null && !d.Nodes.ContainsKey(c.Next))
                {
                    yield return Error("BrokenNext", d.Id, $"{name}: no node '{c.Next}'.");
                }

                if (c.Quest is { } quest && !check.Is<QuestDefinition>(quest))
                {
                    yield return Error("BrokenReference", d.Id, $"{name}: no quest '{quest}'.");
                }
            }
        }
    }
}

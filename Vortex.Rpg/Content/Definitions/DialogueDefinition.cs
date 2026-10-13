using System.Collections.Generic;

namespace Vortex.Rpg.Content.Definitions;

/// <summary>
/// An NPC's dialogue (spec M2a §2.2): nodes of text and choices, opened at the first entry whose quest
/// is in that state for the player, else at "start". The speaker's name is the text &lt;id&gt;.name.
/// Server only: the client sees the node it is shown, never the rest.
/// </summary>
public sealed record DialogueDefinition : ContentDefinition
{
    public List<DialogueEntryDefinition> Entries { get; init; } = [];

    public required Dictionary<string, DialogueNodeDefinition> Nodes { get; init; }
}

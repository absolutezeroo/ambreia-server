using System.Collections.Generic;

namespace Vortex.Rpg.Content.Definitions;

/// <summary>One thing the NPC says, and what the player may answer.</summary>
public sealed record DialogueNodeDefinition
{
    public required string Text { get; init; }

    public List<DialogueChoiceDefinition> Choices { get; init; } = [];
}

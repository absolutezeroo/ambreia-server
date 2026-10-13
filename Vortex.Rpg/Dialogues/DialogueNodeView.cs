using System.Collections.Immutable;

namespace Vortex.Rpg.Dialogues;

/// <summary>A node as one player sees it now: who speaks, what, and the choices open to them.</summary>
public sealed record DialogueNodeView(
    string NodeId,
    string Speaker,
    string Text,
    ImmutableArray<DialogueChoiceView> Choices
);

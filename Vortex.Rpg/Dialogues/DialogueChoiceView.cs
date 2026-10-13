namespace Vortex.Rpg.Dialogues;

/// <summary>A choice as a player may see it: its index in the content's node, its text key, its kind.</summary>
public sealed record DialogueChoiceView(int Index, string Text, string Kind);

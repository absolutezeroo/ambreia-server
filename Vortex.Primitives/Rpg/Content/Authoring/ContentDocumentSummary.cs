namespace Vortex.Primitives.Rpg.Content.Authoring;

/// <summary>One line of a kind's list: where the document lives and whether it is retired.</summary>
public sealed record ContentDocumentSummary(string Id, string Kind, string Path, bool Retired);

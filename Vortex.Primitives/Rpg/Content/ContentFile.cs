namespace Vortex.Primitives.Rpg.Content;

/// <summary>One document of a content pack: its path in the pack (named in findings) and its text.</summary>
public sealed record ContentFile(string Path, string Text);

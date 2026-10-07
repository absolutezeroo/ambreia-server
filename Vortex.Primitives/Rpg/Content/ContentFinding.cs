namespace Vortex.Primitives.Rpg.Content;

/// <summary>
/// One validation result of a content pack (spec Contenu §9): its code, the file and the content it
/// is about when known, and a message for whoever wrote the file.
/// </summary>
public sealed record ContentFinding(
    ContentSeverity Severity,
    string Code,
    string? File,
    string? ContentId,
    string Message
);

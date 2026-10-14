namespace Vortex.Primitives.Rpg.Content.Authoring;

/// <summary>
/// What a write did: the import's answer (written, or refused with the validator's findings), or the
/// reason it never reached the import.
/// </summary>
public sealed record ContentWriteResult(ContentImportResult? Result, string? Error)
{
    /// <summary>The content moved past the write's base version: read it again.</summary>
    public const string Stale = "content.stale";

    /// <summary>No live document has that id.</summary>
    public const string UnknownDocument = "content.unknown_document";

    /// <summary>No kind of that name on this server.</summary>
    public const string UnknownKind = "content.unknown_kind";

    public static ContentWriteResult Refused(string error) => new(null, error);
}

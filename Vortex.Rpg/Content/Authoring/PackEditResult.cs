using Vortex.Primitives.Rpg.Content;
using Vortex.Primitives.Rpg.Content.Authoring;

namespace Vortex.Rpg.Content.Authoring;

/// <summary>The pack a write would import, or why it is refused before any import (content authoring §5).</summary>
public sealed record PackEditResult(ContentPack? Pack, string? Error)
{
    /// <summary>The URL id is no content id, is not the source's, or is not of the kind written.</summary>
    public const string IdMismatch = "content.id_mismatch";

    /// <summary>The source is not a JSON object.</summary>
    public const string BadSource = "content.bad_source";

    /// <summary>The path leaves its kind's folder, or another document has it.</summary>
    public const string BadPath = "content.bad_path";

    /// <summary>A text key outside the document, a bad language, or a value on two lines.</summary>
    public const string BadText = "content.bad_text";

    /// <summary>No live document has that id.</summary>
    public const string UnknownDocument = ContentWriteResult.UnknownDocument;

    public static PackEditResult Refused(string error) => new(null, error);
}

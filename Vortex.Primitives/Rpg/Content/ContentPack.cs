using System.Collections.Generic;

namespace Vortex.Primitives.Rpg.Content;

/// <summary>
/// A whole content pack as pushed (spec Contenu §6): every document, the migration rules
/// (<c>migrations.json</c>, absent = none) and the text files by language (<c>fr</c> → body).
/// </summary>
public sealed record ContentPack(
    IReadOnlyList<ContentFile> Files,
    string? Migrations,
    IReadOnlyDictionary<string, string> Texts
);

using System.Collections.Generic;

namespace Vortex.Primitives.Rpg.Content;

/// <summary>
/// The stored content pack (content authoring §3): every live document at its path with its text as
/// authored, the latest pack's migrations.json, the text files by language, and the version it is.
/// What an export gives git, and what a write starts from.
/// </summary>
public sealed record ContentExport(
    long Version,
    IReadOnlyList<ContentFile> Files,
    string? Migrations,
    IReadOnlyDictionary<string, string> Texts
);

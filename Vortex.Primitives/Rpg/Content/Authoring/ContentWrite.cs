using System.Collections.Generic;

namespace Vortex.Primitives.Rpg.Content.Authoring;

/// <summary>
/// A document created or replaced (content authoring §4): its source, optionally its path, and the
/// texts under its id by language (a language named replaces all its lines; one not named is kept).
/// Written only if the content is still at <see cref="BaseVersion"/>.
/// </summary>
public sealed record ContentWrite(
    string Id,
    string Kind,
    long BaseVersion,
    string? Path,
    string Source,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Texts
);

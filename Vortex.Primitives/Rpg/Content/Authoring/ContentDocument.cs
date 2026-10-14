using System.Collections.Generic;

namespace Vortex.Primitives.Rpg.Content.Authoring;

/// <summary>
/// One document as authored (its file's text) and its texts by language (the keys under its id),
/// read at <see cref="Version"/>: the base version of the write that follows.
/// </summary>
public sealed record ContentDocument(
    long Version,
    string Id,
    string Kind,
    string Path,
    string Source,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Texts
);

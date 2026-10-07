using System;

namespace Vortex.Primitives.World.Editing;

/// <summary>One line of a zone's history.</summary>
public sealed record ZoneVersionInfo(
    int Version,
    int AuthorAccountId,
    DateTime CreatedAt,
    bool Published,
    string Comment
);

using System.Collections.Generic;

namespace Vortex.Primitives.Rpg.Content.Authoring;

/// <summary>A kind's documents, and the content version they are read at.</summary>
public sealed record ContentDocumentList(
    long Version,
    IReadOnlyList<ContentDocumentSummary> Documents
);

using System.Collections.Generic;

namespace Vortex.Primitives.World.Editing;

/// <summary>What a save or a publication did, and why not when it did not.</summary>
public sealed record ZoneSaveResult(
    bool Saved,
    int? Version,
    bool Published,
    IReadOnlyList<ZoneFinding> Findings
);

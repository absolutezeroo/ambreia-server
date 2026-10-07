using System.Collections.Generic;

namespace Vortex.Primitives.Rpg.Content;

/// <summary>The answer to an import (spec Contenu §6). Not imported: nothing was written, and the
/// findings say why. Imported with no change: the version stays and nothing was written.</summary>
public sealed record ContentImportResult(
    bool Imported,
    long PackVersion,
    int Added,
    int Changed,
    int Removed,
    IReadOnlyList<ContentFinding> Findings
);

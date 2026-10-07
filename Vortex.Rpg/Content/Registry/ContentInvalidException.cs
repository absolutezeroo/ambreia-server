using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rpg.Content;

namespace Vortex.Rpg.Content.Registry;

/// <summary>The stored content does not validate: the registry keeps what it served, and the
/// message lists every error.</summary>
public sealed class ContentInvalidException(IReadOnlyList<ContentFinding> findings)
    : Exception(
        "The stored RPG content is invalid:"
            + string.Concat(
                findings.Select(f =>
                    $"{Environment.NewLine}  {f.Code} {f.File} {f.ContentId}: {f.Message}"
                )
            )
    )
{
    public IReadOnlyList<ContentFinding> Findings { get; } = findings;
}

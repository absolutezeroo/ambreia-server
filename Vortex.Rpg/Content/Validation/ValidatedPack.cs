using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Migrations;

namespace Vortex.Rpg.Content.Validation;

/// <summary>What a pack reads as: its definitions by id, its migration rules, its texts by language,
/// and every finding. Only a pack without errors is imported.</summary>
public sealed record ValidatedPack(
    IReadOnlyDictionary<string, ContentDefinition> Definitions,
    IReadOnlyList<ContentMigrationRule> Rules,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Texts,
    IReadOnlyList<ContentFinding> Findings
)
{
    public bool HasErrors => Findings.Any(f => f.Severity == ContentSeverity.Error);
}

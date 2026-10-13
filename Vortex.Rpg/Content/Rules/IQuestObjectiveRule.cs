using System.Collections.Generic;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Content.Rules;

/// <summary>
/// What an objective kind requires of its fields (spec M2a §3.4). Declared by DI by the module that
/// runs the kind, so the import refuses a quest nobody could progress, and the core names no kind.
/// </summary>
public interface IQuestObjectiveRule
{
    /// <summary>The <see cref="QuestObjectiveDefinition.Kind"/> it answers to.</summary>
    public string Kind { get; }

    /// <summary>One sentence per field missing or wrong; empty when the objective is valid.</summary>
    public IEnumerable<string> Problems(QuestObjectiveDefinition objective, ContentCheck check);
}

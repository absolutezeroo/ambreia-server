using System.Collections.Generic;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Content.Rules;

/// <summary>What a reward kind requires of its fields (spec M2a §3.4), declared by the module that grants it.</summary>
public interface IQuestRewardRule
{
    public string Kind { get; }

    public IEnumerable<string> Problems(QuestRewardDefinition reward, ContentCheck check);
}

using System;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;

namespace Vortex.Rpg.Quests.Log;

/// <summary>What the log looks things up in: the quests of the content, the objective kinds of the host.</summary>
public sealed record QuestBook(
    Func<string, QuestDefinition?> Quest,
    Func<string, IQuestObjectiveRule?> Kind
);

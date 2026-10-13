using System.Collections.Generic;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Quests.Objectives;

/// <summary>Kill monsters (spec M2a §2.1): one of a content id, of a zone, of a rank, or any mix.</summary>
public sealed class KillObjective : ICountedObjectiveKind
{
    private static readonly HashSet<string> Ranks = ["normal", "elite", "boss"];

    public string Kind => "kill";

    public int Counts(QuestObjectiveDefinition objective, QuestSignal signal)
    {
        if (signal.Name != QuestSignals.MonsterKilled)
        {
            return 0;
        }

        KillFields f = QuestFields.Read<KillFields>(objective.Fields);
        bool matches =
            (f.Monster is null || signal.Subject.GetValueOrDefault("monster") == f.Monster)
            && (f.Zone is null || signal.Zone == f.Zone)
            && (f.Rank is null || signal.Subject.GetValueOrDefault("rank") == f.Rank);

        return matches ? signal.Amount : 0;
    }

    public IEnumerable<string> Problems(QuestObjectiveDefinition objective, ContentCheck check)
    {
        if (QuestFields.Problem<KillFields>(objective.Fields) is { } problem)
        {
            yield return problem;

            yield break;
        }

        KillFields f = QuestFields.Read<KillFields>(objective.Fields);

        if (f.Monster is null && f.Zone is null && f.Rank is null)
        {
            yield return "names a monster, a zone or a rank.";
        }

        if (f.Monster is not null && !check.Is<MonsterDefinition>(f.Monster))
        {
            yield return $"no monster '{f.Monster}' in the content.";
        }

        if (f.Rank is not null && !Ranks.Contains(f.Rank))
        {
            yield return $"rank '{f.Rank}': normal, elite or boss.";
        }
    }
}

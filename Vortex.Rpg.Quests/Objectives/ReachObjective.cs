using System.Collections.Generic;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Quests.Objectives;

/// <summary>Reach an area of a zone (spec M2a §2.1). The zone and area are the editor's ids: not content.</summary>
public sealed class ReachObjective : ICountedObjectiveKind
{
    public string Kind => "reach";

    public int Counts(QuestObjectiveDefinition objective, QuestSignal signal)
    {
        if (signal.Name != QuestSignals.AreaEntered)
        {
            return 0;
        }

        ReachFields f = QuestFields.Read<ReachFields>(objective.Fields);

        return signal.Zone == f.Zone && signal.Subject.GetValueOrDefault("area") == f.Area
            ? signal.Amount
            : 0;
    }

    public IEnumerable<string> Problems(QuestObjectiveDefinition objective, ContentCheck check) =>
        QuestFields.Problem<ReachFields>(objective.Fields) is { } problem ? [problem] : [];
}

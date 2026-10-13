using System.Collections.Generic;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Quests.Objectives;

/// <summary>Use a decor object of a zone (spec M2a §2.1): a door, a lever, a well with an Interact trigger.</summary>
public sealed class InteractObjective : ICountedObjectiveKind
{
    public string Kind => "interact";

    public int Counts(QuestObjectiveDefinition objective, QuestSignal signal)
    {
        if (signal.Name != QuestSignals.DecorUsed)
        {
            return 0;
        }

        InteractFields f = QuestFields.Read<InteractFields>(objective.Fields);

        return signal.Zone == f.Zone && signal.Subject.GetValueOrDefault("object") == f.Object
            ? signal.Amount
            : 0;
    }

    public IEnumerable<string> Problems(QuestObjectiveDefinition objective, ContentCheck check) =>
        QuestFields.Problem<InteractFields>(objective.Fields) is { } problem ? [problem] : [];
}

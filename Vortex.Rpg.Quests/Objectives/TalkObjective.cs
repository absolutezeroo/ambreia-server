using System.Collections.Generic;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Quests.Objectives;

/// <summary>Talk to an NPC of a zone (spec M2a §2.1); its signal comes with the NPCs (plan M2a-2).</summary>
public sealed class TalkObjective : ICountedObjectiveKind
{
    public string Kind => "talk";

    public int Counts(QuestObjectiveDefinition objective, QuestSignal signal)
    {
        if (signal.Name != QuestSignals.NpcTalked)
        {
            return 0;
        }

        TalkFields f = QuestFields.Read<TalkFields>(objective.Fields);

        return signal.Zone == f.Zone && signal.Subject.GetValueOrDefault("npc") == f.Npc
            ? signal.Amount
            : 0;
    }

    public IEnumerable<string> Problems(QuestObjectiveDefinition objective, ContentCheck check) =>
        QuestFields.Problem<TalkFields>(objective.Fields) is { } problem ? [problem] : [];
}

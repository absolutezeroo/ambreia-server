using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Protocol.Messages.Outgoing.Rpg.Quests;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Quests.Wire;

/// <summary>RpgQuestLog (spec M2a §3.2) from the log: a quest the content no longer has keeps its counters, kind unknown.</summary>
public static class QuestLogComposer
{
    public static RpgQuestLogMessageComposer Of(
        IEnumerable<QuestEntrySnapshot> entries,
        Func<string, QuestDefinition?> quest
    ) =>
        new()
        {
            Quests =
            [
                .. entries.Select(e => new RpgQuestLogEntry
                {
                    QuestId = e.QuestId,
                    State = QuestCodes.Name(e.Status),
                    Objectives =
                    [
                        .. e.Progress.Select(
                            (current, i) =>
                                quest(e.QuestId)?.Objectives.ElementAtOrDefault(i) is { } o
                                    ? new RpgQuestObjectiveLine
                                    {
                                        Kind = o.Kind,
                                        Current = current,
                                        Required = o.Count,
                                    }
                                    : new RpgQuestObjectiveLine
                                    {
                                        Kind = string.Empty,
                                        Current = current,
                                        Required = current,
                                    }
                        ),
                    ],
                }),
            ],
        };
}

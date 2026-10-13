using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Quests.Tests.Support;

/// <summary>Objectives and signals written the way content and producers write them.</summary>
internal static class Pieces
{
    public static QuestObjectiveDefinition Objective(
        string kind,
        int count = 1,
        params (string Key, object Value)[] fields
    ) =>
        new()
        {
            Kind = kind,
            Count = count,
            Fields = fields.ToDictionary(
                f => f.Key,
                f => JsonSerializer.SerializeToElement(f.Value)
            ),
        };

    public static QuestSignal Signal(
        string name,
        string zone,
        params (string Key, string Value)[] subject
    ) => new(name, zone, subject.ToImmutableDictionary(s => s.Key, s => s.Value));
}

using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Quests;

/// <summary>A quest of the log: its state (active, ready, done) and its objective lines.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgQuestLogEntry
{
    [Id(0)]
    public required string QuestId { get; init; }

    [Id(1)]
    public required string State { get; init; }

    [Id(2)]
    public required ImmutableArray<RpgQuestObjectiveLine> Objectives { get; init; }
}

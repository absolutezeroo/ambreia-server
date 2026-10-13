using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Quests;

/// <summary>An objective's kind (for its icon) and its counter.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgQuestObjectiveLine
{
    [Id(0)]
    public required string Kind { get; init; }

    [Id(1)]
    public required int Current { get; init; }

    [Id(2)]
    public required int Required { get; init; }
}

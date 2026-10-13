using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Quests;

/// <summary>A quest action's answer (spec M2a §3.2, 9036): accepted, completed, abandoned or talk, with ok or a refusal code.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgQuestResultMessageComposer : IComposer
{
    [Id(0)]
    public required string Action { get; init; }

    [Id(1)]
    public required string Code { get; init; }

    [Id(2)]
    public required string QuestId { get; init; }
}

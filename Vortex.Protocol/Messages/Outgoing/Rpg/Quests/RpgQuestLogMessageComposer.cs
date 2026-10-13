using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Quests;

/// <summary>The whole quest log (spec M2a §3.2, 9035): after sign-in and after every change.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgQuestLogMessageComposer : IComposer
{
    [Id(0)]
    public required ImmutableArray<RpgQuestLogEntry> Quests { get; init; }
}

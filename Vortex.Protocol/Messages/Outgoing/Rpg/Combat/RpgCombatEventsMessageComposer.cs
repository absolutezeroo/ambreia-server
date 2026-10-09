using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

/// <summary>The hits of one combat tick of a zone (spec M1a §9.2, 9012): one message per tick, to the
/// whole zone, sent only when something was hit.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgCombatEventsMessageComposer : IComposer
{
    [Id(0)]
    public required ImmutableArray<RpgCombatHit> Hits { get; init; }
}

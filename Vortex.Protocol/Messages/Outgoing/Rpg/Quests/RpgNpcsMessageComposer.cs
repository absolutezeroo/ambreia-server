using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Quests;

/// <summary>The zone's NPCs and what each one has for this player (spec M2a §3.2, 9032).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgNpcsMessageComposer : IComposer
{
    [Id(0)]
    public required ImmutableArray<RpgNpcMarker> Npcs { get; init; }
}

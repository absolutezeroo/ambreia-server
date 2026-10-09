using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

/// <summary>The casts that began this tick (spec M1b §10.2, 9020), to the zone.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgCastStartMessageComposer : IComposer
{
    [Id(0)]
    public required ImmutableArray<RpgCastStart> Casts { get; init; }
}

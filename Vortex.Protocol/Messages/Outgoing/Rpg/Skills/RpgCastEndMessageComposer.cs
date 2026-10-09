using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

/// <summary>The casts that ended this tick (spec M1b §10.2, 9021), to the zone.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgCastEndMessageComposer : IComposer
{
    [Id(0)]
    public required ImmutableArray<RpgCastEnd> Ends { get; init; }
}

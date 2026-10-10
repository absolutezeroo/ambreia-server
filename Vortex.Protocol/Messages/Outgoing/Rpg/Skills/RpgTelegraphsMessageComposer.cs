using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

/// <summary>The telegraphs that began this tick (spec M1c §9.2, 9025), to the zone; on entering it,
/// those still under way.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgTelegraphsMessageComposer : IComposer
{
    [Id(0)]
    public required ImmutableArray<RpgTelegraph> Telegraphs { get; init; }
}

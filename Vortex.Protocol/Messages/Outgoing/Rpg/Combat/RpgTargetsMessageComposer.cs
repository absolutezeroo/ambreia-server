using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

/// <summary>Qui vise qui (M1d §2.1, 9027) : complet à l'entrée de zone, puis les changements du tick.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgTargetsMessageComposer : IComposer
{
    [Id(0)]
    public required ImmutableArray<RpgTargetEntry> Entries { get; init; }
}

using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Interaction;

/// <summary>
/// What a player can click in this zone (spec F3b §5, 9008), sent with the entry: every other decor
/// object is frozen, and the client stops showing Habbo's furni info for it.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record RpgZoneInteractivesMessageComposer : IComposer
{
    [Id(0)]
    public required ImmutableArray<RpgZoneInteractive> Objects { get; init; }
}

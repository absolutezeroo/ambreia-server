using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Rpg.Items;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Items;

/// <summary>
/// Everything the character holds (spec Objets §17, 9006): the bag size, the parcel limit, then
/// every copy in the bag, worn, or in a parcel. It replaces the client's whole state.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record RpgInventoryMessageComposer : IComposer
{
    [Id(0)]
    public required int BagSize { get; init; }

    [Id(1)]
    public required int MaxParcels { get; init; }

    [Id(2)]
    public required ImmutableArray<CharacterItemSnapshot> Items { get; init; }
}

using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Pets.Snapshots;

namespace Vortex.Protocol.Messages.Outgoing.Catalog;

[GenerateSerializer, Immutable]
public sealed record SellablePetPalettesMessageComposer : IComposer
{
    [Id(0)]
    public required string ProductCode { get; init; }

    [Id(1)]
    public required ImmutableArray<PetSkinEntry> Palettes { get; init; }
}

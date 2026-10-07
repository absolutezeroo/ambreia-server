using Orleans;
using Vortex.Primitives.Furniture.Enums;

namespace Vortex.Primitives.Catalog.Snapshots;

[GenerateSerializer, Immutable]
public sealed record CatalogProductSnapshot
{
    [Id(0)]
    public required int Id { get; init; }

    [Id(1)]
    public required int OfferId { get; init; }

    [Id(2)]
    public required ProductType ProductType { get; init; }

    [Id(3)]
    public required int FurniDefinitionId { get; init; }

    [Id(4)]
    public required int SpriteId { get; init; }

    [Id(5)]
    public required string? ExtraParam { get; init; }

    [Id(6)]
    public required int Quantity { get; init; }

    [Id(7)]
    public required int UniqueSize { get; init; }

    [Id(8)]
    public required int UniqueRemaining { get; init; }

    // Id 9 was the Builders Membership placement flag, removed in M0. Retired, not reused.
}

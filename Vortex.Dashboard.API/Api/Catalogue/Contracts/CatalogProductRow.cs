namespace Vortex.Dashboard.API.Api.Catalogue.Contracts;

/// <summary>
/// One thing in an offer's bundle.
/// </summary>
/// <param name="FurnitureIconUrl">Built from whatever this product actually is -- a badge and an
/// effect have no furniture definition and are drawn from their ExtraParam instead.</param>
public sealed record CatalogProductRow(
    int Id,
    int ProductType,
    string ProductTypeLabel,
    int? FurnitureDefinitionEntityId,
    string? FurnitureName,
    int? FurnitureSpriteId,
    string? FurnitureIconUrl,
    string? ExtraParam,
    int Quantity,
    int UniqueSize,
    int UniqueRemaining
);

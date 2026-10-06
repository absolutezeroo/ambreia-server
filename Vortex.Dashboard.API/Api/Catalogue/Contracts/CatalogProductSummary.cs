namespace Vortex.Dashboard.API.Api.Catalogue.Contracts;

/// <summary>
/// The one thing a single-product offer gives.
/// </summary>
/// <param name="UniqueRemaining">How many of a limited edition are left; meaningless when
/// <paramref name="UniqueSize"/> is 0.</param>
public sealed record CatalogProductSummary(
    int Id,
    int ProductType,
    string ProductTypeLabel,
    string? FurnitureName,
    string? FurnitureIconUrl,
    int Quantity,
    int UniqueSize,
    int UniqueRemaining
);

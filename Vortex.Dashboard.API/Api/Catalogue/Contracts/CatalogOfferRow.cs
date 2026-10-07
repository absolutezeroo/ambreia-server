namespace Vortex.Dashboard.API.Api.Catalogue.Contracts;

/// <summary>
/// One offer on a page.
/// </summary>
/// <param name="SingleProduct">What the offer actually gives, when it gives exactly one thing.
/// Null for a bundle: most offers hold one product, and inlining that one is what lets the row say
/// what you get without a click per offer. A bundle has to be opened.</param>
public sealed record CatalogOfferRow(
    int Id,
    string LocalizationId,
    int CostCredits,
    int CostCurrency,
    int? CurrencyTypeId,
    string? CurrencyName,
    bool CanGift,
    bool CanBundle,
    int MembershipLevel,
    int DiscountPercent,
    bool Visible,
    int ProductCount,
    CatalogProductSummary? SingleProduct
);

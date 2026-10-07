namespace Vortex.Dashboard.API.Api.Catalogue.Contracts;

/// <summary>One offer, with the page it belongs to named rather than only numbered.</summary>
public sealed record CatalogOfferView(
    int Id,
    int CatalogPageEntityId,
    string PageLocalization,
    int CatalogType,
    string LocalizationId,
    int CostCredits,
    int CostCurrency,
    int? CurrencyTypeId,
    string? CurrencyName,
    bool CanGift,
    bool CanBundle,
    int MembershipLevel,
    int DiscountPercent,
    bool Visible
);

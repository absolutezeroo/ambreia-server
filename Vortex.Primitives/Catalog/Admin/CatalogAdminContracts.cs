using System.Collections.Generic;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Furniture.Enums;

namespace Vortex.Primitives.Catalog.Admin;

/// <summary>
/// Outcome of a catalog admin write: success, or an error code. This service is a plain in-process
/// singleton (not a grain), so there is no need for Orleans serialization attributes here.
/// </summary>
public sealed record CatalogAdminResult(bool Success, int? Id, string? ErrorCode)
{
    public static CatalogAdminResult Ok(int id) => new(true, id, null);

    public static CatalogAdminResult Fail(string errorCode) => new(false, null, errorCode);
}

/// <summary>
/// <paramref name="CatalogType"/> decides which catalog tree this page belongs to (only Normal is
/// left). It is set once at creation and is deliberately
/// absent from <see cref="CatalogPageUpdateSpec"/> — <c>CatalogSnapshotProvider</c> partitions
/// entirely by this column, so changing it after the fact would silently move a page (and every
/// offer/product under it) across that boundary.
/// </summary>
public sealed record CatalogPageCreateSpec(
    CatalogType CatalogType,
    int? ParentId,
    string Localization,
    string? Name,
    int Icon,
    CatalogPageLayout Layout,
    List<string>? ImageData,
    List<string>? TextData,
    int SortOrder,
    bool Visible
);

public sealed record CatalogPageUpdateSpec(
    int? ParentId,
    string Localization,
    string? Name,
    int Icon,
    CatalogPageLayout Layout,
    List<string>? ImageData,
    List<string>? TextData,
    int SortOrder,
    bool Visible
);

/// <summary>
/// <paramref name="PageId"/> is set once at creation only — an offer's catalog type is inherited
/// from its page, so re-parenting is out of scope for this admin surface (see
/// <see cref="CatalogPageCreateSpec"/>'s note on <c>CatalogType</c> for the same reasoning).
/// </summary>
public sealed record CatalogOfferCreateSpec(
    int PageId,
    string LocalizationId,
    int CostCredits,
    int CostCurrency,
    int? CurrencyTypeId,
    bool CanGift,
    bool CanBundle,
    int ClubLevel,
    int DiscountPercent,
    bool Visible
);

public sealed record CatalogOfferUpdateSpec(
    string LocalizationId,
    int CostCredits,
    int CostCurrency,
    int? CurrencyTypeId,
    bool CanGift,
    bool CanBundle,
    int ClubLevel,
    int DiscountPercent,
    bool Visible
);

public sealed record CatalogProductCreateSpec(
    int OfferId,
    ProductType ProductType,
    int? FurnitureDefinitionId,
    string? ExtraParam,
    int Quantity,
    int UniqueSize,
    int UniqueRemaining
);

public sealed record CatalogProductUpdateSpec(
    ProductType ProductType,
    int? FurnitureDefinitionId,
    string? ExtraParam,
    int Quantity,
    int UniqueSize,
    int UniqueRemaining
);

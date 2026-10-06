using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Orleans;
using Vortex.Dashboard.API.Api.Catalogue.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Database.Context;
using Vortex.Database.Entities.Audit;
using Vortex.Database.Entities.Catalog;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Marketplace;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Observability.Metrics;
using Vortex.Observability.Runtime;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Rooms.Grains;

namespace Vortex.Dashboard.API.Api.Catalogue;

/// <summary>
/// What the catalogue pages, offers and products look like to an operator.
/// </summary>
/// <remarks>
/// Two dependencies, which is what this subject actually uses: a context and the asset URL builder,
/// which owns the icon template as well as the icon URLs. It used to be part of a class that took
/// ten for every subject — see <see cref="DashboardReads"/> for why the base carries only the
/// context.
/// </remarks>
internal sealed class CatalogReads(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    DashboardAssetUrls assetUrls
) : DashboardReads(dbContextFactory)
{
    private readonly DashboardAssetUrls _assetUrls = assetUrls;

    /// <summary>Pages at one level of one catalog tree. <c>parentId</c> omitted/blank means the root
    /// level (pages with no parent) of the given <c>catalogType</c> (0=Normal).</summary>
    public Task<CatalogPageList> CatalogPagesAsync(NameValueCollection query, CancellationToken ct)
    {
        CatalogType catalogType = int.TryParse(query["catalogType"], out int catalogTypeValue)
            ? (CatalogType)catalogTypeValue
            : CatalogType.Normal;
        int? parentId = int.TryParse(query["parentId"], out int parsedParentId)
            ? parsedParentId
            : null;

        return QueryAsync<CatalogPageList>(
            async db =>
            {
                var rows = await db
                    .CatalogPages.AsNoTracking()
                    .Where(p => p.CatalogType == catalogType && p.ParentEntityId == parentId)
                    .OrderBy(p => p.SortOrder)
                    .ThenBy(p => p.Localization)
                    .Select(p => new
                    {
                        p.Id,
                        p.ParentEntityId,
                        p.Localization,
                        p.Name,
                        p.Icon,
                        layout = p.Layout.ToLayoutString(),
                        p.SortOrder,
                        p.Visible,
                        childCount = db.CatalogPages.Count(c => c.ParentEntityId == p.Id),
                        offerCount = db.CatalogOffers.Count(o => o.CatalogPageEntityId == p.Id),
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<CatalogPageRow> items = rows.Select(p => new CatalogPageRow(
                        p.Id,
                        p.ParentEntityId,
                        p.Localization,
                        p.Name,
                        p.Icon,
                        BuildCatalogIconUrl(p.Icon),
                        p.layout,
                        p.SortOrder,
                        p.Visible,
                        p.childCount,
                        p.offerCount
                    ))
                    .ToList();

                return new CatalogPageList((int)catalogType, parentId, items.Count, items);
            },
            ct
        );
    }

    public Task<CatalogPageDetail?> CatalogPageDetailAsync(int pageId, CancellationToken ct) =>
        QueryAsync<CatalogPageDetail?>(
            async db =>
            {
                CatalogPageEntity? page = await db
                    .CatalogPages.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == pageId, ct)
                    .ConfigureAwait(false);

                if (page is null)
                {
                    return null;
                }

                var offerRows = await db
                    .CatalogOffers.AsNoTracking()
                    .Where(o => o.CatalogPageEntityId == pageId)
                    .OrderBy(o => o.Id)
                    .Select(o => new
                    {
                        o.Id,
                        o.LocalizationId,
                        o.CostCredits,
                        o.CostCurrency,
                        o.CurrencyTypeId,
                        // By activity-point type, not by row id: see CatalogOfferEntity.CurrencyTypeId.
                        currencyName = db
                            .CurrencyTypes.Where(c =>
                                c.CurrencyType == CurrencyType.ActivityPoints
                                && c.ActivityPointType == o.CurrencyTypeId
                            )
                            .Select(c => c.Name)
                            .FirstOrDefault(),
                        o.CanGift,
                        o.CanBundle,
                        o.ClubLevel,
                        o.DiscountPercent,
                        o.Visible,
                        productCount = db.CatalogProducts.Count(pr =>
                            pr.CatalogOfferEntityId == o.Id
                        ),
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                // Most offers hold exactly one product (a bundle with 2+ is the exception, e.g. a
                // table+chair set) -- fetching that single product's summary here lets the UI show
                // "what you actually get" inline on the offer row, with no extra click/request per
                // row. This is one batched query for every single-product offer on the page, not
                // one query per offer.
                List<int> singleProductOfferIds = offerRows
                    .Where(o => o.productCount == 1)
                    .Select(o => o.Id)
                    .ToList();

                var singleProductRows =
                    singleProductOfferIds.Count == 0
                        ? []
                        : await db
                            .CatalogProducts.AsNoTracking()
                            .Where(p => singleProductOfferIds.Contains(p.CatalogOfferEntityId))
                            .Select(p => new
                            {
                                p.CatalogOfferEntityId,
                                p.Id,
                                productType = (int)p.ProductType,
                                productTypeLabel = p.ProductType.ToString(),
                                furnitureName = p.FurnitureDefinition != null
                                    ? p.FurnitureDefinition.Name
                                    : null,
                                p.ExtraParam,
                                p.Quantity,
                                p.UniqueSize,
                                p.UniqueRemaining,
                            })
                            .ToListAsync(ct)
                            .ConfigureAwait(false);

                Dictionary<int, CatalogProductSummary> singleProductByOfferId =
                    singleProductRows.ToDictionary(
                        p => p.CatalogOfferEntityId,
                        p => new CatalogProductSummary(
                            p.Id,
                            p.productType,
                            p.productTypeLabel,
                            p.furnitureName,
                            _assetUrls.ProductImage(p.productType, p.furnitureName, p.ExtraParam),
                            p.Quantity,
                            p.UniqueSize,
                            p.UniqueRemaining
                        )
                    );

                List<CatalogOfferRow> offers = offerRows
                    .Select(o => new CatalogOfferRow(
                        o.Id,
                        o.LocalizationId,
                        o.CostCredits,
                        o.CostCurrency,
                        o.CurrencyTypeId,
                        o.currencyName,
                        o.CanGift,
                        o.CanBundle,
                        o.ClubLevel,
                        o.DiscountPercent,
                        o.Visible,
                        o.productCount,
                        singleProductByOfferId.GetValueOrDefault(o.Id)
                    ))
                    .ToList();

                string? parentLocalization = page.ParentEntityId is { } parentId
                    ? await db
                        .CatalogPages.AsNoTracking()
                        .Where(p => p.Id == parentId)
                        .Select(p => p.Localization)
                        .FirstOrDefaultAsync(ct)
                        .ConfigureAwait(false)
                    : null;

                return new CatalogPageDetail(
                    page.Id,
                    (int)page.CatalogType,
                    page.ParentEntityId,
                    parentLocalization,
                    page.Localization,
                    page.Name,
                    page.Icon,
                    BuildCatalogIconUrl(page.Icon),
                    page.Layout.ToLayoutString(),
                    page.ImageData,
                    page.TextData,
                    page.SortOrder,
                    page.Visible,
                    offers
                );
            },
            ct
        );

    public Task<CatalogOfferDetail?> CatalogOfferDetailAsync(int offerId, CancellationToken ct) =>
        QueryAsync<CatalogOfferDetail?>(
            async db =>
            {
                CatalogOfferView? offer = await db
                    .CatalogOffers.AsNoTracking()
                    .Where(o => o.Id == offerId)
                    .Select(o => new CatalogOfferView(
                        o.Id,
                        o.CatalogPageEntityId,
                        o.Page.Localization,
                        (int)o.Page.CatalogType,
                        o.LocalizationId,
                        o.CostCredits,
                        o.CostCurrency,
                        o.CurrencyTypeId,
                        db.CurrencyTypes.Where(c =>
                                c.CurrencyType == CurrencyType.ActivityPoints
                                && c.ActivityPointType == o.CurrencyTypeId
                            )
                            .Select(c => c.Name)
                            .FirstOrDefault(),
                        o.CanGift,
                        o.CanBundle,
                        o.ClubLevel,
                        o.DiscountPercent,
                        o.Visible
                    ))
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

                if (offer is null)
                {
                    return null;
                }

                var productRows = await db
                    .CatalogProducts.AsNoTracking()
                    .Where(p => p.CatalogOfferEntityId == offerId)
                    .OrderBy(p => p.Id)
                    .Select(p => new
                    {
                        p.Id,
                        productType = (int)p.ProductType,
                        productTypeLabel = p.ProductType.ToString(),
                        p.FurnitureDefinitionEntityId,
                        furnitureName = p.FurnitureDefinition != null
                            ? p.FurnitureDefinition.Name
                            : null,
                        furnitureSpriteId = p.FurnitureDefinition != null
                            ? (int?)p.FurnitureDefinition.SpriteId
                            : null,
                        p.ExtraParam,
                        p.Quantity,
                        p.UniqueSize,
                        p.UniqueRemaining,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                // BuildFurniIconUrl isn't translatable to SQL, so the icon URL is attached in a
                // second pass over the already-materialized rows (same two-step shape as
                // FurnitureDefinitionsAsync).
                List<CatalogProductRow> products = productRows
                    .Select(p => new CatalogProductRow(
                        p.Id,
                        p.productType,
                        p.productTypeLabel,
                        p.FurnitureDefinitionEntityId,
                        p.furnitureName,
                        p.furnitureSpriteId,
                        _assetUrls.ProductImage(p.productType, p.furnitureName, p.ExtraParam),
                        p.ExtraParam,
                        p.Quantity,
                        p.UniqueSize,
                        p.UniqueRemaining
                    ))
                    .ToList();

                return new CatalogOfferDetail(offer.Id, offer, products);
            },
            ct
        );

    public Task<CatalogCurrencyList> CatalogCurrencyTypesAsync(CancellationToken ct) =>
        QueryAsync<CatalogCurrencyList>(
            async db =>
            {
                List<CatalogCurrency> rows = await db
                    .CurrencyTypes.AsNoTracking()
                    .Where(c => c.Enabled)
                    .OrderBy(c => c.Id)
                    .Select(c => new CatalogCurrency(
                        c.Id,
                        c.Name,
                        c.CurrencyType.ToString(),
                        c.ActivityPointType
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                return new CatalogCurrencyList(rows.Count, rows);
            },
            ct
        );

    /// <summary>
    /// Exposes the resolved <c>{id}</c> URL template so the icon picker can build candidate URLs
    /// client-side and probe them via normal &lt;img&gt; load/error events -- there is no manifest
    /// of which icon ids actually exist on the asset host, so "does this id have a real icon" can
    /// only be answered by letting the browser try to load it.
    /// </summary>
    public CatalogIconTemplate CatalogIconTemplate() => new(_assetUrls.CatalogIconTemplate);

    private string? BuildCatalogIconUrl(int iconId) => _assetUrls.CatalogIcon(iconId);
}

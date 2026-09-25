using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Catalog;
using Vortex.Observability.Diagnostics;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Admin;
using Vortex.Primitives.Catalog.Providers;
using Vortex.Primitives.Catalog.Tags;
using Vortex.Primitives.Players.Enums.Wallet;

namespace Vortex.Dashboard.API.Admin.Catalogue;

/// <summary>
/// CRUD for catalog_pages/catalog_offers/catalog_products. Not a grain — catalog rows aren't
/// grain-owned and there is no per-item concurrency need the way <c>VoucherGrain</c> has, so a plain
/// singleton opening a short-lived <see cref="VortexDbContext"/> per call is enough.
///
/// The live catalog clients see is an immutable in-memory snapshot rebuilt only via
/// <c>ICatalogSnapshotProvider{TTag}.ReloadAsync</c> — every write here reloads BOTH the Normal and
/// Builders Club providers afterwards, unconditionally, rather than trying to infer which one the
/// change belongs to. This is deliberately simple and safe: a page's own CatalogType decides which
/// tree it is in, but working that out correctly for every one of the 6 write paths (including
/// products, which are two hops removed from CatalogType) is exactly the kind of live/DB sync bug
/// this codebase has already been bitten by once (see AGENTS.md). Reload is cheap (full snapshot
/// rebuild, not per-row) and writes here are a low-frequency admin action, so reloading both is not
/// a meaningful cost.
/// </summary>
internal sealed class CatalogAdminService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    ICatalogSnapshotProvider<NormalCatalog> normalProvider,
    ICatalogSnapshotProvider<BuildersClubCatalog> buildersClubProvider,
    ILogger<CatalogAdminService> logger
) : ICatalogAdminService
{
    public async Task<CatalogAdminResult> CreatePageAsync(
        CatalogPageCreateSpec spec,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        if (spec.ParentId is { } parentId)
        {
            CatalogPageEntity? parent = await db
                .CatalogPages.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == parentId, ct)
                .ConfigureAwait(false);

            if (parent is null)
            {
                return CatalogAdminResult.Fail("parent_not_found");
            }

            if (parent.CatalogType != spec.CatalogType)
            {
                return CatalogAdminResult.Fail("parent_catalog_type_mismatch");
            }
        }

        CatalogPageEntity entity = new()
        {
            CatalogType = spec.CatalogType,
            ParentEntityId = spec.ParentId,
            Localization = spec.Localization,
            Name = spec.Name,
            Icon = spec.Icon,
            Layout = spec.Layout,
            ImageData = spec.ImageData,
            TextData = spec.TextData,
            SortOrder = spec.SortOrder,
            Visible = spec.Visible,
        };

        db.CatalogPages.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadBothAsync(ct).ConfigureAwait(false);

        return CatalogAdminResult.Ok(entity.Id);
    }

    public async Task<CatalogAdminResult> UpdatePageAsync(
        int pageId,
        CatalogPageUpdateSpec spec,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        CatalogPageEntity? entity = await db
            .CatalogPages.FirstOrDefaultAsync(p => p.Id == pageId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return CatalogAdminResult.Fail("page_not_found");
        }

        if (spec.ParentId == pageId)
        {
            return CatalogAdminResult.Fail("page_cannot_parent_itself");
        }

        if (spec.ParentId is { } parentId)
        {
            CatalogPageEntity? parent = await db
                .CatalogPages.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == parentId, ct)
                .ConfigureAwait(false);

            if (parent is null)
            {
                return CatalogAdminResult.Fail("parent_not_found");
            }

            if (parent.CatalogType != entity.CatalogType)
            {
                return CatalogAdminResult.Fail("parent_catalog_type_mismatch");
            }
        }

        entity.ParentEntityId = spec.ParentId;
        entity.Localization = spec.Localization;
        entity.Name = spec.Name;
        entity.Icon = spec.Icon;
        entity.Layout = spec.Layout;
        entity.ImageData = spec.ImageData;
        entity.TextData = spec.TextData;
        entity.SortOrder = spec.SortOrder;
        entity.Visible = spec.Visible;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadBothAsync(ct).ConfigureAwait(false);

        return CatalogAdminResult.Ok(entity.Id);
    }

    public async Task<CatalogAdminResult> DeletePageAsync(int pageId, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        CatalogPageEntity? entity = await db
            .CatalogPages.FirstOrDefaultAsync(p => p.Id == pageId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return CatalogAdminResult.Fail("page_not_found");
        }

        bool hasChildren = await db
            .CatalogPages.AnyAsync(p => p.ParentEntityId == pageId, ct)
            .ConfigureAwait(false);

        if (hasChildren)
        {
            return CatalogAdminResult.Fail("page_has_children");
        }

        bool hasOffers = await db
            .CatalogOffers.AnyAsync(o => o.CatalogPageEntityId == pageId, ct)
            .ConfigureAwait(false);

        if (hasOffers)
        {
            return CatalogAdminResult.Fail("page_has_offers");
        }

        // The children guard above cannot see soft-deleted children — the global DeletedAt filter
        // hides them — but parent_id still references this page and the FK refuses the delete.
        // They are already gone from every read, so they are detached rather than blocking.
        List<CatalogPageEntity> deadChildren = await db
            .CatalogPages.IgnoreQueryFilters()
            .Where(p => p.ParentEntityId == pageId && p.DeletedAt != null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (CatalogPageEntity deadChild in deadChildren)
        {
            deadChild.ParentEntityId = null;
        }

        db.CatalogPages.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadBothAsync(ct).ConfigureAwait(false);

        return CatalogAdminResult.Ok(pageId);
    }

    /// <summary>
    /// An offer's <c>CurrencyTypeId</c> is an activity-point type (5 diamonds), not a
    /// <c>currency_types</c> row id — see <see cref="CatalogOfferEntity.CurrencyTypeId"/>. It must
    /// name one the hotel has a currency row for, or the wallet refuses every purchase of it.
    /// </summary>
    private static async Task<bool> IsKnownActivityPointTypeAsync(
        VortexDbContext db,
        int? activityPointType,
        CancellationToken ct
    ) =>
        activityPointType is not { } type
        || await db
            .CurrencyTypes.AsNoTracking()
            .AnyAsync(
                c => c.CurrencyType == CurrencyType.ActivityPoints && c.ActivityPointType == type,
                ct
            )
            .ConfigureAwait(false);

    public async Task<CatalogAdminResult> CreateOfferAsync(
        CatalogOfferCreateSpec spec,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        CatalogPageEntity? page = await db
            .CatalogPages.FirstOrDefaultAsync(p => p.Id == spec.PageId, ct)
            .ConfigureAwait(false);

        if (page is null)
        {
            return CatalogAdminResult.Fail("page_not_found");
        }

        if (!await IsKnownActivityPointTypeAsync(db, spec.CurrencyTypeId, ct).ConfigureAwait(false))
        {
            return CatalogAdminResult.Fail("currency_type_not_found");
        }

        CatalogOfferEntity entity = new()
        {
            CatalogPageEntityId = spec.PageId,
            Page = page,
            LocalizationId = spec.LocalizationId,
            CostCredits = spec.CostCredits,
            CostCurrency = spec.CostCurrency,
            CurrencyTypeId = spec.CurrencyTypeId,
            CanGift = spec.CanGift,
            CanBundle = spec.CanBundle,
            ClubLevel = spec.ClubLevel,
            DiscountPercent = spec.DiscountPercent,
            Visible = spec.Visible,
        };

        db.CatalogOffers.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadBothAsync(ct).ConfigureAwait(false);

        return CatalogAdminResult.Ok(entity.Id);
    }

    public async Task<CatalogAdminResult> UpdateOfferAsync(
        int offerId,
        CatalogOfferUpdateSpec spec,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        CatalogOfferEntity? entity = await db
            .CatalogOffers.FirstOrDefaultAsync(o => o.Id == offerId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return CatalogAdminResult.Fail("offer_not_found");
        }

        if (!await IsKnownActivityPointTypeAsync(db, spec.CurrencyTypeId, ct).ConfigureAwait(false))
        {
            return CatalogAdminResult.Fail("currency_type_not_found");
        }

        entity.LocalizationId = spec.LocalizationId;
        entity.CostCredits = spec.CostCredits;
        entity.CostCurrency = spec.CostCurrency;
        entity.CurrencyTypeId = spec.CurrencyTypeId;
        entity.CanGift = spec.CanGift;
        entity.CanBundle = spec.CanBundle;
        entity.ClubLevel = spec.ClubLevel;
        entity.DiscountPercent = spec.DiscountPercent;
        entity.Visible = spec.Visible;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadBothAsync(ct).ConfigureAwait(false);

        return CatalogAdminResult.Ok(entity.Id);
    }

    public async Task<CatalogAdminResult> DeleteOfferAsync(int offerId, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        CatalogOfferEntity? entity = await db
            .CatalogOffers.FirstOrDefaultAsync(o => o.Id == offerId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return CatalogAdminResult.Fail("offer_not_found");
        }

        bool hasProducts = await db
            .CatalogProducts.AnyAsync(p => p.CatalogOfferEntityId == offerId, ct)
            .ConfigureAwait(false);

        if (hasProducts)
        {
            return CatalogAdminResult.Fail("offer_has_products");
        }

        db.CatalogOffers.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadBothAsync(ct).ConfigureAwait(false);

        return CatalogAdminResult.Ok(offerId);
    }

    public async Task<CatalogAdminResult> CreateProductAsync(
        CatalogProductCreateSpec spec,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        CatalogOfferEntity? offer = await db
            .CatalogOffers.FirstOrDefaultAsync(o => o.Id == spec.OfferId, ct)
            .ConfigureAwait(false);

        if (offer is null)
        {
            return CatalogAdminResult.Fail("offer_not_found");
        }

        if (spec.FurnitureDefinitionId is { } definitionId)
        {
            bool definitionExists = await db
                .FurnitureDefinitions.AsNoTracking()
                .AnyAsync(f => f.Id == definitionId, ct)
                .ConfigureAwait(false);

            if (!definitionExists)
            {
                return CatalogAdminResult.Fail("furniture_definition_not_found");
            }
        }

        CatalogProductEntity entity = new()
        {
            CatalogOfferEntityId = spec.OfferId,
            Offer = offer,
            ProductType = spec.ProductType,
            FurnitureDefinitionEntityId = spec.FurnitureDefinitionId,
            ExtraParam = spec.ExtraParam,
            Quantity = spec.Quantity,
            UniqueSize = spec.UniqueSize,
            UniqueRemaining = spec.UniqueRemaining,
            BuildersClubEligible = spec.BuildersClubEligible,
        };

        db.CatalogProducts.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadBothAsync(ct).ConfigureAwait(false);

        return CatalogAdminResult.Ok(entity.Id);
    }

    public async Task<CatalogAdminResult> UpdateProductAsync(
        int productId,
        CatalogProductUpdateSpec spec,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        CatalogProductEntity? entity = await db
            .CatalogProducts.FirstOrDefaultAsync(p => p.Id == productId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return CatalogAdminResult.Fail("product_not_found");
        }

        if (spec.FurnitureDefinitionId is { } definitionId)
        {
            bool definitionExists = await db
                .FurnitureDefinitions.AsNoTracking()
                .AnyAsync(f => f.Id == definitionId, ct)
                .ConfigureAwait(false);

            if (!definitionExists)
            {
                return CatalogAdminResult.Fail("furniture_definition_not_found");
            }
        }

        entity.ProductType = spec.ProductType;
        entity.FurnitureDefinitionEntityId = spec.FurnitureDefinitionId;
        entity.ExtraParam = spec.ExtraParam;
        entity.Quantity = spec.Quantity;
        entity.UniqueSize = spec.UniqueSize;
        entity.UniqueRemaining = spec.UniqueRemaining;
        entity.BuildersClubEligible = spec.BuildersClubEligible;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadBothAsync(ct).ConfigureAwait(false);

        return CatalogAdminResult.Ok(entity.Id);
    }

    public async Task<CatalogAdminResult> DeleteProductAsync(int productId, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        CatalogProductEntity? entity = await db
            .CatalogProducts.FirstOrDefaultAsync(p => p.Id == productId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return CatalogAdminResult.Fail("product_not_found");
        }

        db.CatalogProducts.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadBothAsync(ct).ConfigureAwait(false);

        return CatalogAdminResult.Ok(productId);
    }

    private async Task ReloadBothAsync(CancellationToken ct)
    {
        try
        {
            await normalProvider.ReloadAsync(ct).ConfigureAwait(false);
            await buildersClubProvider.ReloadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The DB write already committed at this point -- the live snapshot is now stale until
            // the next successful reload or a full restart. Never swallow this: it is exactly the
            // "DB write not reflected in live state" bug class called out in AGENTS.md.
            logger.LogError(
                VortexEventIds.CatalogSnapshotReloadFailed,
                ex,
                "Catalog snapshot reload failed after an admin write committed -- live catalog is now stale until the next reload or restart"
            );
            throw;
        }
    }
}

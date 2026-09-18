using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Navigator;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Navigator.Admin;
using Vortex.Primitives.Navigator.Enums;

namespace Vortex.Dashboard.API.Admin.Hotel;

/// <summary>
/// CRUD for the navigator configuration tables. A plain singleton opening a short-lived
/// <see cref="VortexDbContext"/> per call — these rows are not grain-owned and admin writes are
/// rare. Every write ends with <see cref="INavigatorProvider.ReloadAsync"/>: the provider's snapshot
/// is built once at reference-data load and never re-read, so a committed row that skipped the
/// reload would stay invisible to players until the next restart.
/// </summary>
internal sealed class NavigatorAdminService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    INavigatorProvider provider,
    ILogger<NavigatorAdminService> logger
) : INavigatorAdminService
{
    /// <summary>
    /// The tabs the client asks for and the blocks each one is expected to carry. Every code is one
    /// the client already localizes (see <see cref="NavigatorSearchCodes"/>); inventing a code here
    /// would render a block titled with the raw string.
    /// </summary>
    private static readonly (
        string Code,
        NavigatorQueryType Query,
        string[] Blocks
    )[] DefaultTabs =
    [
        (
            NavigatorSearchCodes.HotelView,
            NavigatorQueryType.Popular,
            [
                NavigatorSearchCodes.Popular,
                NavigatorSearchCodes.StaffPicks,
                NavigatorSearchCodes.HighestScore,
                NavigatorSearchCodes.Recommended,
                NavigatorSearchCodes.TopPromotions,
            ]
        ),
        (
            NavigatorSearchCodes.MyWorldView,
            NavigatorQueryType.MyRooms,
            [
                NavigatorSearchCodes.MyRooms,
                NavigatorSearchCodes.Favourites,
                NavigatorSearchCodes.HistoryFrequent,
                NavigatorSearchCodes.WithRights,
                NavigatorSearchCodes.MyGuildBases,
                NavigatorSearchCodes.FriendsRooms,
            ]
        ),
        (
            NavigatorSearchCodes.OfficialView,
            NavigatorQueryType.StaffPicks,
            [NavigatorSearchCodes.Official, NavigatorSearchCodes.TopPromotions]
        ),
        (
            NavigatorSearchCodes.RoomAdsView,
            NavigatorQueryType.RoomAds,
            [NavigatorSearchCodes.RoomAds, NavigatorSearchCodes.TopPromotions]
        ),
    ];

    public async Task<NavigatorAdminResult> CreateContextAsync(
        NavigatorContextSpec spec,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(spec.SearchCode))
        {
            return NavigatorAdminResult.Fail("search_code_required");
        }

        string code = spec.SearchCode.Trim();

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        bool exists = await db
            .NavigatorTopLevelContexts.AnyAsync(
                c => c.SearchCode == code && c.DeletedAt == null,
                ct
            )
            .ConfigureAwait(false);

        if (exists)
        {
            // Two rows for one code make ResolveQueryType a coin toss (it keeps the first of the
            // group), so the duplicate is refused rather than silently half-applied.
            return NavigatorAdminResult.Fail("search_code_already_configured");
        }

        NavigatorTopLevelContextEntity entity = new()
        {
            SearchCode = code,
            Visible = spec.Visible,
            QueryType = spec.QueryType,
            OrderNum = spec.OrderNum,
        };

        db.NavigatorTopLevelContexts.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return NavigatorAdminResult.Ok(entity.Id);
    }

    public async Task<NavigatorAdminResult> UpdateContextAsync(
        int contextId,
        NavigatorContextSpec spec,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(spec.SearchCode))
        {
            return NavigatorAdminResult.Fail("search_code_required");
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        NavigatorTopLevelContextEntity? entity = await db
            .NavigatorTopLevelContexts.FirstOrDefaultAsync(c => c.Id == contextId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return NavigatorAdminResult.Fail("context_not_found");
        }

        entity.SearchCode = spec.SearchCode.Trim();
        entity.Visible = spec.Visible;
        entity.QueryType = spec.QueryType;
        entity.OrderNum = spec.OrderNum;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return NavigatorAdminResult.Ok(entity.Id);
    }

    public async Task<NavigatorAdminResult> DeleteContextAsync(int contextId, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        NavigatorTopLevelContextEntity? entity = await db
            .NavigatorTopLevelContexts.FirstOrDefaultAsync(c => c.Id == contextId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return NavigatorAdminResult.Fail("context_not_found");
        }

        bool hasQuickLinks = await db
            .NavigatorQuickLinks.AnyAsync(q => q.TopLevelContextEntityId == contextId, ct)
            .ConfigureAwait(false);

        if (hasQuickLinks)
        {
            // The quick links carry the FK; removing the tab first would either orphan them or hit
            // the constraint. Steer the operator to emptying the tab (or just hiding it) instead.
            return NavigatorAdminResult.Fail("context_has_quick_links");
        }

        db.NavigatorTopLevelContexts.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return NavigatorAdminResult.Ok(contextId);
    }

    public async Task<NavigatorAdminResult> CreateQuickLinkAsync(
        NavigatorQuickLinkSpec spec,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(spec.SearchCode))
        {
            return NavigatorAdminResult.Fail("search_code_required");
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        bool contextExists = await db
            .NavigatorTopLevelContexts.AnyAsync(c => c.Id == spec.TopLevelContextId, ct)
            .ConfigureAwait(false);

        if (!contextExists)
        {
            return NavigatorAdminResult.Fail("context_not_found");
        }

        NavigatorQuickLinkEntity entity = new()
        {
            TopLevelContextEntityId = spec.TopLevelContextId,
            SearchCode = spec.SearchCode.Trim(),
            Filter = spec.Filter ?? string.Empty,
            Localization = spec.Localization ?? string.Empty,
            QueryType = spec.QueryType,
            OrderNum = spec.OrderNum,
            ForceClosed = spec.ForceClosed,
        };

        db.NavigatorQuickLinks.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return NavigatorAdminResult.Ok(entity.Id);
    }

    public async Task<NavigatorAdminResult> UpdateQuickLinkAsync(
        int quickLinkId,
        NavigatorQuickLinkSpec spec,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(spec.SearchCode))
        {
            return NavigatorAdminResult.Fail("search_code_required");
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        NavigatorQuickLinkEntity? entity = await db
            .NavigatorQuickLinks.FirstOrDefaultAsync(q => q.Id == quickLinkId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return NavigatorAdminResult.Fail("quick_link_not_found");
        }

        bool contextExists = await db
            .NavigatorTopLevelContexts.AnyAsync(c => c.Id == spec.TopLevelContextId, ct)
            .ConfigureAwait(false);

        if (!contextExists)
        {
            return NavigatorAdminResult.Fail("context_not_found");
        }

        entity.TopLevelContextEntityId = spec.TopLevelContextId;
        entity.SearchCode = spec.SearchCode.Trim();
        entity.Filter = spec.Filter ?? string.Empty;
        entity.Localization = spec.Localization ?? string.Empty;
        entity.QueryType = spec.QueryType;
        entity.OrderNum = spec.OrderNum;
        entity.ForceClosed = spec.ForceClosed;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return NavigatorAdminResult.Ok(entity.Id);
    }

    public async Task<NavigatorAdminResult> DeleteQuickLinkAsync(
        int quickLinkId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        NavigatorQuickLinkEntity? entity = await db
            .NavigatorQuickLinks.FirstOrDefaultAsync(q => q.Id == quickLinkId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return NavigatorAdminResult.Fail("quick_link_not_found");
        }

        db.NavigatorQuickLinks.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return NavigatorAdminResult.Ok(quickLinkId);
    }

    public async Task<NavigatorAdminResult> CreateFlatCategoryAsync(
        NavigatorFlatCategorySpec spec,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(spec.Name))
        {
            return NavigatorAdminResult.Fail("name_required");
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        NavigatorFlatCategoryEntity entity = new()
        {
            Name = spec.Name.Trim(),
            Visible = spec.Visible,
            Automatic = spec.Automatic,
            AutomaticCategory = spec.AutomaticCategory,
            GlobalCategory = spec.GlobalCategory,
            StaffOnly = spec.StaffOnly,
            MinRank = Math.Max(0, spec.MinRank),
            OrderNum = spec.OrderNum,
        };

        db.NavigatorFlatCategories.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return NavigatorAdminResult.Ok(entity.Id);
    }

    public async Task<NavigatorAdminResult> UpdateFlatCategoryAsync(
        int categoryId,
        NavigatorFlatCategorySpec spec,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(spec.Name))
        {
            return NavigatorAdminResult.Fail("name_required");
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        NavigatorFlatCategoryEntity? entity = await db
            .NavigatorFlatCategories.FirstOrDefaultAsync(c => c.Id == categoryId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return NavigatorAdminResult.Fail("category_not_found");
        }

        entity.Name = spec.Name.Trim();
        entity.Visible = spec.Visible;
        entity.Automatic = spec.Automatic;
        entity.AutomaticCategory = spec.AutomaticCategory;
        entity.GlobalCategory = spec.GlobalCategory;
        entity.StaffOnly = spec.StaffOnly;
        entity.MinRank = Math.Max(0, spec.MinRank);
        entity.OrderNum = spec.OrderNum;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return NavigatorAdminResult.Ok(entity.Id);
    }

    public async Task<NavigatorAdminResult> DeleteFlatCategoryAsync(
        int categoryId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        NavigatorFlatCategoryEntity? entity = await db
            .NavigatorFlatCategories.FirstOrDefaultAsync(c => c.Id == categoryId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return NavigatorAdminResult.Fail("category_not_found");
        }

        bool inUse = await db
            .Rooms.AnyAsync(r => r.NavigatorCategoryEntityId == categoryId, ct)
            .ConfigureAwait(false);

        if (inUse)
        {
            // Rooms point at the category by id; dropping it would leave them filed under a category
            // that no longer resolves. Hiding it (Visible = false) is the reversible equivalent.
            return NavigatorAdminResult.Fail("category_in_use");
        }

        db.NavigatorFlatCategories.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return NavigatorAdminResult.Ok(categoryId);
    }

    public async Task<NavigatorAdminResult> CreateEventCategoryAsync(
        NavigatorEventCategorySpec spec,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(spec.Name))
        {
            return NavigatorAdminResult.Fail("name_required");
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        NavigatorEventCategoryEntity entity = new()
        {
            Name = spec.Name.Trim(),
            Visible = spec.Visible,
        };

        db.NavigatorEventCategories.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return NavigatorAdminResult.Ok(entity.Id);
    }

    public async Task<NavigatorAdminResult> UpdateEventCategoryAsync(
        int categoryId,
        NavigatorEventCategorySpec spec,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(spec.Name))
        {
            return NavigatorAdminResult.Fail("name_required");
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        NavigatorEventCategoryEntity? entity = await db
            .NavigatorEventCategories.FirstOrDefaultAsync(c => c.Id == categoryId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return NavigatorAdminResult.Fail("category_not_found");
        }

        entity.Name = spec.Name.Trim();
        entity.Visible = spec.Visible;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return NavigatorAdminResult.Ok(entity.Id);
    }

    public async Task<NavigatorAdminResult> DeleteEventCategoryAsync(
        int categoryId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        NavigatorEventCategoryEntity? entity = await db
            .NavigatorEventCategories.FirstOrDefaultAsync(c => c.Id == categoryId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return NavigatorAdminResult.Fail("category_not_found");
        }

        bool inUse = await db
            .RoomAdvertisements.AnyAsync(a => a.CategoryId == categoryId, ct)
            .ConfigureAwait(false);

        if (inUse)
        {
            return NavigatorAdminResult.Fail("category_in_use");
        }

        db.NavigatorEventCategories.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return NavigatorAdminResult.Ok(categoryId);
    }

    public async Task<NavigatorAdminResult> SeedDefaultsAsync(CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        List<NavigatorTopLevelContextEntity> existingContexts = await db
            .NavigatorTopLevelContexts.Include(c => c.QuickLinks)
            .Where(c => c.DeletedAt == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int created = 0;
        int order = 0;

        foreach ((string code, NavigatorQueryType query, string[] blocks) in DefaultTabs)
        {
            NavigatorTopLevelContextEntity? context = existingContexts.Find(c =>
                string.Equals(c.SearchCode, code, StringComparison.Ordinal)
            );

            if (context is null)
            {
                context = new NavigatorTopLevelContextEntity
                {
                    SearchCode = code,
                    Visible = true,
                    QueryType = query,
                    OrderNum = order,
                };

                db.NavigatorTopLevelContexts.Add(context);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                created++;
            }

            int blockOrder = 0;

            foreach (string block in blocks)
            {
                bool blockExists = (context.QuickLinks ?? []).Exists(q =>
                    q.DeletedAt == null
                    && string.Equals(q.SearchCode, block, StringComparison.Ordinal)
                );

                if (!blockExists)
                {
                    db.NavigatorQuickLinks.Add(
                        new NavigatorQuickLinkEntity
                        {
                            TopLevelContextEntityId = context.Id,
                            SearchCode = block,
                            Filter = string.Empty,
                            Localization = string.Empty,
                            QueryType =
                                NavigatorSearchCodes.QueryTypeBySearchCode.GetValueOrDefault(
                                    block,
                                    NavigatorQueryType.AllRooms
                                ),
                            OrderNum = blockOrder,
                        }
                    );
                    created++;
                }

                blockOrder++;
            }

            order++;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);

        return NavigatorAdminResult.Ok(created);
    }

    private async Task ReloadAsync(CancellationToken ct)
    {
        try
        {
            await provider.ReloadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The write already committed — the navigator snapshot every player searches against is
            // now stale until the next reload or restart. Never swallow this: it is the "DB write not
            // reflected in live state" bug class called out in AGENTS.md.
            logger.LogError(
                ex,
                "Navigator snapshot reload failed after an admin write committed -- the live navigator is now stale until the next reload or restart"
            );
            throw;
        }
    }
}

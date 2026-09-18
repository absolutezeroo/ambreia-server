using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Api.Hotel.Contracts;
using Vortex.Database.Context;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Navigator.Enums;

namespace Vortex.Dashboard.API.Api.Hotel;

/// <summary>
/// The navigator's own configuration: the tabs the client asks for, the blocks inside them, and the
/// two category tables.
/// <para>
/// This is the one dashboard surface where "no rows" is the interesting state: an unseeded hotel
/// answers every navigator request with an empty left pane, and a tab with no quick links renders
/// blank however healthy the room list is. Both are reported explicitly rather than shown as an
/// empty table the operator has to interpret.
/// </para>
/// </summary>
internal sealed class NavigatorReads(IDbContextFactory<VortexDbContext> dbContextFactory)
    : DashboardReads(dbContextFactory)
{
    public Task<NavigatorSetup> NavigatorConfigAsync(CancellationToken ct) =>
        QueryAsync<NavigatorSetup>(
            async db =>
            {
                var contexts = await db
                    .NavigatorTopLevelContexts.AsNoTracking()
                    .OrderBy(c => c.OrderNum)
                    .ThenBy(c => c.Id)
                    .Select(c => new
                    {
                        c.Id,
                        c.SearchCode,
                        c.Visible,
                        queryType = (int)c.QueryType,
                        queryTypeLabel = c.QueryType.ToString(),
                        c.OrderNum,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                var quickLinks = await db
                    .NavigatorQuickLinks.AsNoTracking()
                    .OrderBy(q => q.TopLevelContextEntityId)
                    .ThenBy(q => q.OrderNum)
                    .Select(q => new
                    {
                        q.Id,
                        contextId = q.TopLevelContextEntityId,
                        q.SearchCode,
                        q.Filter,
                        q.Localization,
                        queryType = (int)q.QueryType,
                        queryTypeLabel = q.QueryType.ToString(),
                        q.OrderNum,
                        q.ForceClosed,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<NavigatorFlatCategoryRow> flatCategories = await db
                    .NavigatorFlatCategories.AsNoTracking()
                    .OrderBy(c => c.OrderNum)
                    .ThenBy(c => c.Id)
                    .Select(c => new NavigatorFlatCategoryRow(
                        c.Id,
                        c.Name,
                        c.Visible,
                        c.Automatic,
                        c.AutomaticCategory,
                        c.GlobalCategory,
                        c.StaffOnly,
                        c.MinRank,
                        c.OrderNum,
                        db.Rooms.Count(r =>
                            r.NavigatorCategoryEntityId == c.Id && r.DeletedAt == null
                        )
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                DateTime now = DateTime.UtcNow;

                List<NavigatorEventCategoryRow> eventCategories = await db
                    .NavigatorEventCategories.AsNoTracking()
                    .OrderBy(c => c.Id)
                    .Select(c => new NavigatorEventCategoryRow(
                        c.Id,
                        c.Name,
                        c.Visible,
                        db.RoomAdvertisements.Count(a => a.CategoryId == c.Id && a.ExpiresAt > now)
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                Dictionary<int, int> linkCountByContext = quickLinks
                    .GroupBy(q => q.contextId)
                    .ToDictionary(g => g.Key, g => g.Count());

                // The four codes the client itself asks for. A hotel missing one of these has a tab
                // that answers nothing at all, which is invisible from the table alone.
                List<string> missingTabs = NavigatorSearchCodes
                    .TopLevelViews.Where(view =>
                        !contexts.Exists(c =>
                            string.Equals(c.SearchCode, view, StringComparison.Ordinal)
                        )
                    )
                    .OrderBy(view => view, StringComparer.Ordinal)
                    .ToList();

                List<NavigatorEmptyTab> emptyTabs = contexts
                    .Where(c => linkCountByContext.GetValueOrDefault(c.Id) == 0)
                    .Select(c => new NavigatorEmptyTab(c.Id, c.SearchCode))
                    .ToList();

                List<NavigatorContextRow> items = contexts
                    .Select(c => new NavigatorContextRow(
                        c.Id,
                        c.SearchCode,
                        c.Visible,
                        c.queryType,
                        c.queryTypeLabel,
                        c.OrderNum,
                        NavigatorSearchCodes.QueryTypeBySearchCode.ContainsKey(c.SearchCode),
                        quickLinks
                            .Where(q => q.contextId == c.Id)
                            .Select(q => new NavigatorQuickLinkRow(
                                q.Id,
                                q.SearchCode,
                                q.Filter,
                                q.Localization,
                                q.queryType,
                                q.queryTypeLabel,
                                q.OrderNum,
                                q.ForceClosed,
                                NavigatorSearchCodes.QueryTypeBySearchCode.ContainsKey(q.SearchCode)
                            ))
                            .ToList()
                    ))
                    .ToList();

                return new NavigatorSetup(
                    new NavigatorHealth(
                        contexts.Count,
                        quickLinks.Count,
                        flatCategories.Count,
                        eventCategories.Count,
                        missingTabs,
                        emptyTabs,
                        contexts.Count > 0 && quickLinks.Count > 0
                    ),
                    items,
                    flatCategories,
                    eventCategories,
                    NavigatorSearchCodes
                        .QueryTypeBySearchCode.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => new NavigatorSearchCodeOption(
                            pair.Key,
                            (int)pair.Value,
                            pair.Value.ToString(),
                            NavigatorSearchCodes.TopLevelViews.Contains(pair.Key)
                        ))
                        .ToList(),
                    Enum.GetValues<NavigatorQueryType>()
                        .Select(value => new NavigatorQueryTypeOption((int)value, value.ToString()))
                        .ToList()
                );
            },
            ct
        );
}

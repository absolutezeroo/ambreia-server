using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Api.Progression.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Database.Context;
using Vortex.Primitives.Furniture.Enums;

namespace Vortex.Dashboard.API.Api.Progression;

/// <summary>
/// Read surface for the prize pools. The admin CRUD lives in
/// <see cref="Operations.LootTableOperations"/>; here we only read.
///
/// The share percentages are computed server-side rather than left to the page: an operator typing
/// weights 6/3/1 has written 60/30/10 and needs to see that, and the competing set is not obvious —
/// an entry with no variant competes for every variant of its pool, so its denominator differs from
/// a variant-locked one.
/// </summary>
internal sealed class LootTableReads(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    DashboardAssetUrls assetUrls
) : DashboardReads(dbContextFactory)
{
    private readonly DashboardAssetUrls _assetUrls = assetUrls;

    private const string LootAwardedAction = "loot.awarded";

    public Task<LootTableContent> LootTablesAsync(CancellationToken ct) =>
        QueryAsync<LootTableContent>(
            async db =>
            {
                List<LootTableRow> pools = await db
                    .LootTables.AsNoTracking()
                    .OrderBy(p => p.Code)
                    .Select(p => new LootTableRow(
                        p.Id,
                        p.Code,
                        p.Name,
                        p.Variants,
                        p.Notes,
                        p.Enabled
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                var entries = await db
                    .LootTableEntries.AsNoTracking()
                    .Where(e => e.LootTableEntity != null)
                    .OrderByDescending(e => e.Weight)
                    .ThenBy(e => e.Id)
                    .Select(e => new
                    {
                        e.Id,
                        poolId = e.LootTableEntityId,
                        pool = e.LootTableEntity!.Code,
                        e.Variant,
                        productType = e.ProductType.ToString(),
                        furnitureDefinitionId = e.FurnitureDefinitionEntityId,
                        e.ExtraParam,
                        e.Weight,
                        e.Enabled,
                        furnitureName = db
                            .FurnitureDefinitions.Where(f => f.Id == e.FurnitureDefinitionEntityId)
                            .Select(f => f.Name)
                            .FirstOrDefault(),
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<LootTableEntryRow> entryRows = entries
                    .Select(e => new LootTableEntryRow(
                        e.Id,
                        e.poolId,
                        e.pool,
                        e.Variant,
                        e.productType,
                        e.furnitureDefinitionId,
                        e.ExtraParam,
                        e.Weight,
                        e.Enabled,
                        e.furnitureName,
                        // An operator recognises a sofa, not definition id 4312.
                        e.furnitureName
                            is null
                            ? null
                            : _assetUrls.FurniIcon(e.furnitureName)
                    ))
                    .ToList();

                // The competing set for an entry is: same pool, and either variantless (competes
                // everywhere) or locked to the same variant. Computing it per (pool, variant) group
                // is what makes the number an operator reads match what the picker actually does.
                List<LootTableWeightTotal> totals = entries
                    .Where(e => e.Enabled)
                    .GroupBy(e => new { e.poolId, e.Variant })
                    .Select(g => new LootTableWeightTotal(
                        g.Key.poolId,
                        g.Key.Variant,
                        g.Sum(e => e.Weight),
                        g.Count()
                    ))
                    .OrderBy(g => g.PoolId)
                    .ThenBy(g => g.Variant, StringComparer.Ordinal)
                    .ToList();

                var bindings = await db
                    .LootTableBindings.AsNoTracking()
                    .Where(b => b.LootTableEntity != null)
                    .OrderBy(b => b.FurnitureDefinitionEntityId)
                    .Select(b => new
                    {
                        b.Id,
                        furnitureDefinitionId = b.FurnitureDefinitionEntityId,
                        pool = b.LootTableEntity!.Code,
                        b.HitsRequired,
                        b.Enabled,
                        furnitureName = db
                            .FurnitureDefinitions.Where(f => f.Id == b.FurnitureDefinitionEntityId)
                            .Select(f => f.Name)
                            .FirstOrDefault(),
                        furnitureLogic = db
                            .FurnitureDefinitions.Where(f => f.Id == b.FurnitureDefinitionEntityId)
                            .Select(f => f.Logic)
                            .FirstOrDefault(),
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<LootTableBindingRow> bindingRows = bindings
                    .Select(b => new LootTableBindingRow(
                        b.Id,
                        b.furnitureDefinitionId,
                        b.pool,
                        b.HitsRequired,
                        b.Enabled,
                        b.furnitureName,
                        b.furnitureLogic,
                        b.furnitureName is null ? null : _assetUrls.FurniIcon(b.furnitureName)
                    ))
                    .ToList();

                return new LootTableContent(
                    new LootTableList(pools.Count, pools),
                    new LootTableEntryList(entryRows.Count, entryRows),
                    totals,
                    new LootTableBindingList(bindingRows.Count, bindingRows),
                    [
                        ProductType.Floor.ToString(),
                        ProductType.Wall.ToString(),
                        ProductType.Effect.ToString(),
                        ProductType.Premium.ToString(),
                    ]
                );
            },
            ct
        );

    /// <summary>
    /// What each pool really paid out over the window, from the <c>loot.awarded</c> audit rows the
    /// grant grain writes. This is the half a weights table cannot tell you: a pool can be tuned
    /// correctly and still pay out nothing because the furniture bound to it is unreachable.
    /// </summary>
    public Task<LootTableStats> LootTableStatsAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        QueryAsync<LootTableStats>(
            async db =>
            {
                int days = int.TryParse(query["days"], out int parsed)
                    ? Math.Clamp(parsed, 1, 90)
                    : 7;
                DateTime since = DateTime.UtcNow.AddDays(-days);

                List<string?> payloads = await db
                    .AuditEvents.AsNoTracking()
                    .Where(a => a.OccurredAt >= since && a.Action == LootAwardedAction)
                    .Select(a => a.Data)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                Dictionary<string, Dictionary<int, int>> drawsByPool = new(StringComparer.Ordinal);
                Dictionary<string, Dictionary<string, int>> drawsBySource = new(
                    StringComparer.Ordinal
                );

                foreach (string? payload in payloads)
                {
                    if (string.IsNullOrWhiteSpace(payload))
                    {
                        continue;
                    }

                    string? pool;
                    int entryId;
                    string source;

                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(payload);
                        JsonElement root = document.RootElement;

                        pool = root.TryGetProperty("pool", out JsonElement p)
                            ? p.GetString()
                            : null;
                        entryId = root.TryGetProperty("entryId", out JsonElement e)
                            ? e.GetInt32()
                            : 0;
                        source = root.TryGetProperty("source", out JsonElement s)
                            ? s.GetString() ?? string.Empty
                            : string.Empty;
                    }
                    catch (JsonException)
                    {
                        // A malformed audit payload is a bug elsewhere; dropping the row here keeps
                        // the page readable instead of failing the whole window.
                        continue;
                    }

                    if (string.IsNullOrEmpty(pool))
                    {
                        continue;
                    }

                    Dictionary<int, int> byEntry = drawsByPool.TryGetValue(
                        pool,
                        out Dictionary<int, int>? existing
                    )
                        ? existing
                        : drawsByPool[pool] = [];

                    byEntry[entryId] = byEntry.GetValueOrDefault(entryId) + 1;

                    Dictionary<string, int> bySource = drawsBySource.TryGetValue(
                        pool,
                        out Dictionary<string, int>? existingSources
                    )
                        ? existingSources
                        : drawsBySource[pool] = new Dictionary<string, int>(StringComparer.Ordinal);

                    bySource[source] = bySource.GetValueOrDefault(source) + 1;
                }

                List<LootTableDraws> pools = drawsByPool
                    .Select(p => new LootTableDraws(
                        p.Key,
                        p.Value.Values.Sum(),
                        p.Value.OrderByDescending(e => e.Value)
                            .Select(e => new LootEntryDraws(e.Key, e.Value))
                            .ToList(),
                        drawsBySource.TryGetValue(p.Key, out Dictionary<string, int>? sources)
                            ? sources
                                .OrderByDescending(s => s.Value)
                                .Select(s => new LootSourceDraws(s.Key, s.Value))
                                .ToList()
                            : []
                    ))
                    .OrderByDescending(p => p.Draws)
                    .ToList();

                return new LootTableStats(days, payloads.Count, pools);
            },
            ct
        );
}

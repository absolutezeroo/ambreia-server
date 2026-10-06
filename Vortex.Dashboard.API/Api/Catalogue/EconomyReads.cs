using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Dashboard.API.Api.Catalogue.Contracts;
using Vortex.Dashboard.API.Api.Hotel.Contracts;
using Vortex.Dashboard.API.Api.Safety.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Database.Context;
using Vortex.Database.Entities.Audit;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Marketplace;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Observability.Configuration;
using Vortex.Observability.Metrics;
using Vortex.Observability.Runtime;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Grains;

namespace Vortex.Dashboard.API.Api.Catalogue;

internal sealed partial class EconomyReads(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    DashboardAssetUrls assetUrls
) : DashboardReads(dbContextFactory)
{
    private readonly DashboardAssetUrls _assetUrls = assetUrls;

    public Task<EconomyLedgerPage> EconomyAsync(NameValueCollection query, CancellationToken ct) =>
        QueryAsync<EconomyLedgerPage>(
            async db =>
            {
                int limit = QueryValues.Limit(query["limit"], 50, 500);
                int page = QueryValues.Page(query["page"]);
                int offset = Math.Max(0, (page - 1) * limit);
                IQueryable<EconomyLedgerEntity> q = db.EconomyLedger.AsNoTracking();

                DateTime? since = TimeWindow.ParseDateTime(query["since"]);
                DateTime? until = TimeWindow.ParseDateTime(query["until"]);

                if (since is not null)
                {
                    q = q.Where(l => l.OccurredAt >= since.Value);
                }

                if (until is not null)
                {
                    q = q.Where(l => l.OccurredAt <= until.Value);
                }

                if (int.TryParse(query["player"], out int player))
                {
                    q = q.Where(l => l.PlayerId == player);
                }

                int total = await q.CountAsync(ct).ConfigureAwait(false);

                var rows = await q.OrderByDescending(l => l.OccurredAt)
                    .Skip(offset)
                    .Take(limit)
                    .Select(l => new
                    {
                        l.Id,
                        l.OccurredAt,
                        l.PlayerId,
                        l.Currency,
                        l.ActivityPointType,
                        l.Delta,
                        l.BalanceAfter,
                        reason = l.Reason.ToString(),
                        l.RefId,
                        l.CorrelationId,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<int> playerIds = DisplayNameQueries.NormalizeIds(
                    rows.Select(l => (long?)l.PlayerId)
                );

                Dictionary<int, string> playerNames = await db.PlayerNamesAsync(playerIds, ct)
                    .ConfigureAwait(false);

                List<EconomyLedgerEntry> rowsWithNames = rows.Select(l => new EconomyLedgerEntry(
                        l.Id,
                        l.OccurredAt,
                        l.PlayerId,
                        DisplayNameQueries.ResolvePlayerName(playerNames, l.PlayerId),
                        l.Currency,
                        l.ActivityPointType,
                        l.Delta,
                        l.BalanceAfter,
                        l.reason,
                        l.RefId,
                        l.CorrelationId
                    ))
                    .ToList();

                return new EconomyLedgerPage(rows.Count, page, limit, total, offset, rowsWithNames);
            },
            ct
        );

    /// <summary>
    /// Per-currency spend/earn trend over calendar-aligned buckets (day/month/year — not the
    /// fixed-TimeSpan buckets <see cref="ResolveBucketSize"/> uses, since months/years aren't a
    /// fixed duration). Backs the Economy tab's spend charts: "how many credits/silver/emeralds/
    /// activity points were spent per day/month/year". Groups by the ledger's own <c>Currency</c>
    /// string rather than the fixed <see cref="CurrencyType"/> enum — currency display names are
    /// admin-configurable via the <c>currency_types</c> table (see
    /// <c>PlayerWalletGrain.DescribeCurrency</c>), so whatever labels are actually in the ledger
    /// (which may not be the enum names) are what should drive the series.
    /// </summary>
    public Task<EconomyTrends> EconomyTrendsAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        QueryAsync<EconomyTrends>(
            async db =>
            {
                (DateTime since, DateTime until) = TimeWindow.Resolve(query, DateTime.UtcNow);
                string granularity = TimeWindow.Granularity(query["granularity"]);

                List<EconomyTrendRow> rows = await EconomyTrendQuery(db, since, until)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                string[] currencies = rows.Select(r => r.Currency)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(c => c, StringComparer.Ordinal)
                    .ToArray();

                List<EconomyTrendSeries> series = currencies
                    .Select(currency => new EconomyTrendSeries(
                        currency,
                        BuildEconomyTrendPoints(
                            rows.Where(r => r.Currency == currency).ToList(),
                            since,
                            until,
                            granularity
                        )
                    ))
                    .ToList();

                Dictionary<string, EconomyCurrencyTotals> totals = currencies.ToDictionary(
                    currency => currency,
                    currency =>
                    {
                        List<EconomyTrendRow> currencyRows = rows.Where(r => r.Currency == currency)
                            .ToList();

                        long spend = currencyRows.Sum(r => r.Spend);
                        long earned = currencyRows.Sum(r => r.Earned);

                        return new EconomyCurrencyTotals(
                            spend,
                            earned,
                            earned - spend,
                            currencyRows.Sum(r => r.TransactionCount)
                        );
                    }
                );

                List<EconomySpendCategory> categories = await BuildSpendCategoriesAsync(
                        db,
                        since,
                        until,
                        ct
                    )
                    .ConfigureAwait(false);

                return new EconomyTrends(
                    new ReportWindow(since, until, granularity),
                    currencies,
                    series,
                    totals,
                    categories
                );
            },
            ct
        );

    /// <summary>
    /// "What was this spend actually for" — the ledger's own <c>Reason</c> column is just
    /// Debit/Grant/Adjustment, too coarse to answer that. The business context (catalog purchase,
    /// marketplace sale, admin grant, ...) lives on the
    /// <see cref="AuditEventEntity"/> published for the same operation, sharing the same ambient
    /// correlation id (see
    /// <c>ChannelEconomyLedger</c>/<c>ChannelAuditSink</c>, both stamp
    /// <c>IVortexContextAccessor.Current.CorrelationId</c> when the caller doesn't set one
    /// explicitly). Joining on that id attributes each debit to its originating action.
    /// </summary>
    private static async Task<List<EconomySpendCategory>> BuildSpendCategoriesAsync(
        VortexDbContext db,
        DateTime since,
        DateTime until,
        CancellationToken ct
    )
    {
        List<EconomySpendCategoryRow> spendRows = await SpendCategoryQuery(db, since, until)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return spendRows
            .Select(r => new EconomySpendCategory(
                r.Currency,
                r.Action ?? "uncategorized",
                r.Spend,
                r.TransactionCount
            ))
            .OrderByDescending(x => x.Spend)
            .ToList();
    }

    /// <summary>Marketplace sales summary: active/sold counts, credit volume, a sales timeline, and
    /// top sellers by volume. There is no dashboard visibility into the marketplace today.</summary>
    public Task<MarketplaceSummary> MarketplaceSummaryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        QueryAsync<MarketplaceSummary>(
            async db =>
            {
                (DateTime since, DateTime until) = TimeWindow.Resolve(query, DateTime.UtcNow);
                string granularity = TimeWindow.Granularity(query["granularity"]);

                // Summed by the database, one row per (day, seller). Both aggregates below fold up
                // from that: day is finer than any bucket the timeline offers, and a seller's totals
                // are the sum of their days.
                List<MarketplaceSaleRow> sold = await MarketplaceSalesQuery(db, since, until)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                int activeCount = await db
                    .MarketplaceOffers.AsNoTracking()
                    .CountAsync(o => o.State == MarketplaceOfferState.Active, ct)
                    .ConfigureAwait(false);

                Dictionary<DateTime, (int sales, long volume)> bucketMap = new();

                foreach (MarketplaceSaleRow row in sold)
                {
                    DateTime bucket = TimeWindow.Bucket(row.Day, granularity);
                    (int sales, long volume) current = bucketMap.TryGetValue(
                        bucket,
                        out (int sales, long volume) existing
                    )
                        ? existing
                        : (0, 0L);

                    bucketMap[bucket] = (current.sales + row.Sales, current.volume + row.Volume);
                }

                List<MarketplaceSalePoint> timeline = bucketMap
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new MarketplaceSalePoint(
                        pair.Key.ToString("O"),
                        TimeWindow.Label(pair.Key, granularity),
                        pair.Value.sales,
                        pair.Value.volume
                    ))
                    .ToList();

                int soldCount = sold.Sum(s => s.Sales);
                long soldVolume = sold.Sum(s => s.Volume);

                List<int> sellerIds = DisplayNameQueries.NormalizeIds(
                    sold.Select(s => (int?)s.SellerId)
                );
                Dictionary<int, string> sellerNames = await db.PlayerNamesAsync(sellerIds, ct)
                    .ConfigureAwait(false);

                List<MarketplaceSeller> topSellers = sold.GroupBy(s => s.SellerId)
                    .Select(g => new MarketplaceSeller(
                        g.Key,
                        DisplayNameQueries.ResolvePlayerName(sellerNames, (int?)g.Key),
                        g.Sum(s => s.Sales),
                        g.Sum(s => s.Volume)
                    ))
                    .OrderByDescending(s => s.Volume)
                    .Take(10)
                    .ToList();

                return new MarketplaceSummary(
                    new ReportWindow(since, until, granularity),
                    new MarketplaceTotals(
                        activeCount,
                        // sold holds one row per (day, seller) now, so the sale count is the sum of
                        // the grouped counts -- not the row count -- and the mean price is the
                        // volume over that, which is what Average(price) computed before.
                        soldCount,
                        soldVolume,
                        soldCount > 0 ? (double)soldVolume / soldCount : 0d
                    ),
                    timeline,
                    topSellers
                );
            },
            ct
        );

    public Task<ClubSubscriptions> ClubSubscriptionsAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        QueryAsync<ClubSubscriptions>(
            async db =>
            {
                DateTime nowUtc = DateTime.UtcNow;
                (DateTime since, DateTime until) = TimeWindow.Resolve(query, nowUtc);

                var subscriptions = await db
                    .PlayerSubscriptions.AsNoTracking()
                    .Select(s => new
                    {
                        playerId = s.PlayerEntityId,
                        type = s.SubscriptionType,
                        level = s.Level,
                        s.ExpiresAt,
                        s.TotalMonths,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<int> playerIds = DisplayNameQueries.NormalizeIds(
                    subscriptions.Select(s => (long?)s.playerId)
                );
                Dictionary<int, string> playerNames = await db.PlayerNamesAsync(playerIds, ct)
                    .ConfigureAwait(false);

                var activeSubscriptions = subscriptions.Where(s => s.ExpiresAt > nowUtc).ToList();
                int totalSubscriptions = subscriptions.Count;
                int inactiveCount = totalSubscriptions - activeSubscriptions.Count;
                int expiringIn7Days = activeSubscriptions.Count(s =>
                    s.ExpiresAt <= nowUtc.AddDays(7)
                );
                int expiringIn30Days = activeSubscriptions.Count(s =>
                    s.ExpiresAt <= nowUtc.AddDays(30)
                );
                double activeRate =
                    totalSubscriptions > 0
                        ? Math.Round((double)activeSubscriptions.Count / totalSubscriptions, 4)
                        : 0d;

                List<ClubSubscriptionTypeBreakdown> byType = subscriptions
                    .GroupBy(s => s.type)
                    .Select(g =>
                    {
                        var activeByType = g.Where(s => s.ExpiresAt > nowUtc).ToList();
                        double averageRemainingDays =
                            activeByType.Count > 0
                                ? Math.Round(
                                    activeByType
                                        .Select(s => (s.ExpiresAt - nowUtc).TotalDays)
                                        .Average(),
                                    2
                                )
                                : 0d;

                        double averageTotalMonths = Math.Round(
                            g.Select(s => s.TotalMonths).DefaultIfEmpty(0).Average(),
                            2
                        );

                        return new ClubSubscriptionTypeBreakdown(
                            g.Key.ToString(),
                            g.Count(),
                            activeByType.Count,
                            g.Count() - activeByType.Count,
                            averageRemainingDays,
                            averageTotalMonths
                        );
                    })
                    .OrderBy(x => x.Type, StringComparer.Ordinal)
                    .ToList();

                var events = await db
                    .AuditEvents.AsNoTracking()
                    .Where(e =>
                        e.Category == AuditCategory.Economy
                        && e.OccurredAt >= since
                        && e.OccurredAt <= until
                        && (
                            e.Action == "economy.hc.purchase"
                            || e.Action == "economy.hc.renew"
                            || e.Action == "economy.hc.expired"
                        )
                    )
                    .Select(e => new
                    {
                        e.OccurredAt,
                        e.Action,
                        e.ActorPlayerId,
                        e.Data,
                    })
                    .OrderBy(e => e.OccurredAt)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                // ponytail: grouped in memory, and it has to be -- the numbers being summed live
                // inside the audit event's JSON payload, not in a column the database can group
                // by. The window bound above is what keeps it honest; the real fix is a column.
                List<ClubSubscriptionEvent> clubEvents = events
                    .Select(e => new ClubSubscriptionEvent(
                        e.OccurredAt,
                        e.Action,
                        e.ActorPlayerId,
                        e.Data
                    ))
                    .ToList();

                List<int> actorIds = DisplayNameQueries.NormalizeIds(
                    clubEvents.Select(e => e.ActorPlayerId)
                );
                Dictionary<int, string> actorNames = await db.PlayerNamesAsync(actorIds, ct)
                    .ConfigureAwait(false);

                List<ClubSubscriptionEventRow> enrichedEvents = clubEvents
                    .Select(e =>
                    {
                        ClubSubscriptionPayload? payload = ParseClubSubscriptionPayload(e.Data);

                        return new ClubSubscriptionEventRow(
                            e.OccurredAt,
                            e.Action,
                            DisplayNameQueries.ToPlayerId(e.ActorPlayerId),
                            DisplayNameQueries.ResolvePlayerName(
                                actorNames,
                                DisplayNameQueries.ToPlayerId(e.ActorPlayerId)
                            ),
                            payload?.Months,
                            payload?.TotalMonths,
                            payload?.CreditCost,
                            payload?.IsRenewal,
                            payload?.IsVip
                        );
                    })
                    .OrderByDescending(e => e.OccurredAt)
                    .ToList();

                int purchases = clubEvents.Count(e => e.Action == "economy.hc.purchase");
                int renewals = clubEvents.Count(e => e.Action == "economy.hc.renew");
                int expired = clubEvents.Count(e => e.Action == "economy.hc.expired");
                double renewalShare =
                    purchases + renewals > 0
                        ? Math.Round((double)renewals / (purchases + renewals), 4)
                        : 0d;

                TimeSpan bucketSize = TimeWindow.BucketSize(since, until);
                List<ClubLifecyclePoint> lifecycle = BuildSubscriptionTimeline(
                    clubEvents.Select(e => (e.OccurredAt, e.Action)).ToList(),
                    since,
                    until,
                    bucketSize
                );

                List<ClubMonthsBreakdown> byMonths = enrichedEvents
                    .Where(e => e.Months is not null)
                    .GroupBy(e => e.Months!.Value)
                    .Select(g => new ClubMonthsBreakdown(
                        g.Key,
                        g.Count(),
                        g.Count(e => e.Action == "economy.hc.purchase"),
                        g.Count(e => e.Action == "economy.hc.renew"),
                        g.Count(e => e.Action == "economy.hc.expired")
                    ))
                    .OrderBy(g => g.Months)
                    .ToList();

                List<ClubSubscriptionEventRow> recentEvents = enrichedEvents.Take(30).ToList();

                List<ClubExpiringSubscription> topExpiring = activeSubscriptions
                    .Where(s => s.ExpiresAt <= nowUtc.AddDays(14))
                    .OrderBy(s => s.ExpiresAt)
                    .Take(10)
                    .Select(s => new ClubExpiringSubscription(
                        s.playerId,
                        DisplayNameQueries.ResolvePlayerName(playerNames, s.playerId),
                        s.type.ToString(),
                        s.level,
                        s.TotalMonths,
                        s.ExpiresAt,
                        Math.Round(Math.Max(0, (s.ExpiresAt - nowUtc).TotalDays), 2)
                    ))
                    .ToList();

                return new ClubSubscriptions(
                    new ModerationWindow(since, until),
                    new ClubSubscriptionTotals(
                        totalSubscriptions,
                        activeSubscriptions.Count,
                        inactiveCount,
                        expiringIn7Days,
                        expiringIn30Days,
                        activeRate
                    ),
                    byType,
                    topExpiring,
                    new ClubSubscriptionLifecycle(
                        new ClubLifecycleTotals(purchases, renewals, expired, renewalShare),
                        byMonths,
                        recentEvents,
                        lifecycle
                    )
                );
            },
            ct
        );

    public Task<RentableSpaceAuditPage> RentableSpacesAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        QueryAsync<RentableSpaceAuditPage>(
            async db =>
            {
                int limit = QueryValues.Limit(query["limit"], 50, 500);
                int page = QueryValues.Page(query["page"]);
                int offset = Math.Max(0, (page - 1) * limit);

                DateTime? since = TimeWindow.ParseDateTime(query["since"]);
                DateTime? until = TimeWindow.ParseDateTime(query["until"]);

                IQueryable<AuditEventEntity> q = db
                    .AuditEvents.AsNoTracking()
                    .Where(a => a.Category == AuditCategory.RentableSpace && a.DeletedAt == null);

                if (since is not null)
                {
                    q = q.Where(a => a.OccurredAt >= since.Value);
                }

                if (until is not null)
                {
                    q = q.Where(a => a.OccurredAt <= until.Value);
                }

                if (int.TryParse(query["player"], out int playerFilter))
                {
                    q = q.Where(a =>
                        a.ActorPlayerId == playerFilter || a.TargetPlayerId == playerFilter
                    );
                }

                if (int.TryParse(query["room"], out int roomFilter))
                {
                    q = q.Where(a => a.RoomId == roomFilter);
                }

                int total = await q.CountAsync(ct).ConfigureAwait(false);

                var rows = await q.OrderByDescending(a => a.OccurredAt)
                    .Skip(offset)
                    .Take(limit)
                    .Select(a => new
                    {
                        a.Id,
                        a.OccurredAt,
                        a.Action,
                        a.ActorPlayerId,
                        a.TargetPlayerId,
                        a.RoomId,
                        a.ItemId,
                        a.Data,
                        a.CorrelationId,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                DateTime now = DateTime.UtcNow;

                int activeCount = await db
                    .RoomRentableSpaces.AsNoTracking()
                    .CountAsync(
                        s =>
                            s.RenterPlayerEntityId != null
                            && s.RentedUntil > now
                            && s.DeletedAt == null,
                        ct
                    )
                    .ConfigureAwait(false);

                List<int> playerIds = DisplayNameQueries.NormalizeIds(
                    rows.SelectMany(r => new[] { r.ActorPlayerId, r.TargetPlayerId })
                );

                Dictionary<int, string> playerNames = await db.PlayerNamesAsync(playerIds, ct)
                    .ConfigureAwait(false);

                List<RentableSpaceAuditEntry> rowsWithNames = rows.Select(
                        r => new RentableSpaceAuditEntry(
                            r.Id,
                            r.OccurredAt,
                            r.Action,
                            r.ActorPlayerId,
                            DisplayNameQueries.ResolvePlayerName(playerNames, r.ActorPlayerId),
                            r.TargetPlayerId,
                            DisplayNameQueries.ResolvePlayerName(playerNames, r.TargetPlayerId),
                            r.RoomId,
                            r.ItemId,
                            r.Data,
                            r.CorrelationId
                        )
                    )
                    .ToList();

                return new RentableSpaceAuditPage(
                    activeCount,
                    rows.Count,
                    page,
                    limit,
                    total,
                    offset,
                    rowsWithNames
                );
            },
            ct
        );

    private sealed record ClubSubscriptionEvent(
        DateTime OccurredAt,
        string Action,
        long? ActorPlayerId,
        string? Data
    );

    private sealed record ClubSubscriptionPayload(
        int? Months,
        int? TotalMonths,
        int? CreditCost,
        bool? IsVip,
        bool? IsRenewal
    );

    private static List<ClubLifecyclePoint> BuildSubscriptionTimeline(
        IReadOnlyList<(DateTime OccurredAt, string Action)> events,
        DateTime since,
        DateTime until,
        TimeSpan bucketSize
    )
    {
        if (events.Count == 0)
        {
            return [];
        }

        Dictionary<DateTime, (int purchases, int renewals, int expired)> bucketMap =
            new Dictionary<DateTime, (int purchases, int renewals, int expired)>();

        DateTime cursor = TimeWindow.TimelineBucket(since, bucketSize);
        DateTime end = TimeWindow.TimelineBucket(until, bucketSize);

        while (cursor <= end)
        {
            bucketMap[cursor] = (purchases: 0, renewals: 0, expired: 0);
            cursor = cursor.Add(bucketSize);
        }

        foreach ((DateTime OccurredAt, string Action) evt in events)
        {
            DateTime bucket = TimeWindow.TimelineBucket(evt.OccurredAt, bucketSize);
            (int purchases, int renewals, int expired) counts = bucketMap.TryGetValue(
                bucket,
                out (int purchases, int renewals, int expired) current
            )
                ? current
                : (purchases: 0, renewals: 0, expired: 0);

            counts = evt.Action switch
            {
                "economy.hc.purchase" => (counts.purchases + 1, counts.renewals, counts.expired),
                "economy.hc.renew" => (counts.purchases, counts.renewals + 1, counts.expired),
                "economy.hc.expired" => (counts.purchases, counts.renewals, counts.expired + 1),
                _ => counts,
            };

            bucketMap[bucket] = counts;
        }

        return bucketMap
            .OrderBy(pair => pair.Key)
            .Select(pair => new ClubLifecyclePoint(
                pair.Key.ToString("O"),
                TimeWindow.TimelineLabel(pair.Key, bucketSize),
                pair.Value.purchases,
                pair.Value.renewals,
                pair.Value.expired
            ))
            .ToList();
    }

    /// <summary>One day of one currency, already summed by the database.</summary>
    internal sealed record EconomyTrendRow(
        DateTime Day,
        string Currency,
        long Spend,
        long Earned,
        int TransactionCount
    );

    /// <summary>One day of one seller's marketplace sales, already summed by the database.</summary>
    internal sealed record MarketplaceSaleRow(DateTime Day, int SellerId, int Sales, long Volume);

    /// <summary>One (currency, originating action) pair of spend, already summed by the database.</summary>
    internal sealed record EconomySpendCategoryRow(
        string Currency,
        string? Action,
        long Spend,
        int TransactionCount
    );

    private static List<EconomyTrendPoint> BuildEconomyTrendPoints(
        IReadOnlyList<EconomyTrendRow> rows,
        DateTime since,
        DateTime until,
        string granularity
    )
    {
        Dictionary<DateTime, (long spend, long earned, int count)> bucketMap = new();

        DateTime cursor = TimeWindow.Bucket(since, granularity);
        DateTime end = TimeWindow.Bucket(until, granularity);

        while (cursor <= end)
        {
            bucketMap[cursor] = (0, 0, 0);
            cursor = TimeWindow.NextBucket(cursor, granularity);
        }

        foreach (EconomyTrendRow row in rows)
        {
            DateTime bucket = TimeWindow.Bucket(row.Day, granularity);
            (long spend, long earned, int count) current = bucketMap.TryGetValue(
                bucket,
                out (long spend, long earned, int count) existing
            )
                ? existing
                : (0, 0, 0);

            bucketMap[bucket] = (
                current.spend + row.Spend,
                current.earned + row.Earned,
                current.count + row.TransactionCount
            );
        }

        return bucketMap
            .OrderBy(pair => pair.Key)
            .Select(pair => new EconomyTrendPoint(
                pair.Key.ToString("O"),
                TimeWindow.Label(pair.Key, granularity),
                pair.Value.spend,
                pair.Value.earned,
                pair.Value.earned - pair.Value.spend,
                pair.Value.count
            ))
            .ToList();
    }

    private static ClubSubscriptionPayload? ParseClubSubscriptionPayload(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(data);
            JsonElement root = doc.RootElement;

            return new ClubSubscriptionPayload(
                JsonValues.Int(root, "months"),
                JsonValues.Int(root, "totalMonths"),
                JsonValues.Int(root, "creditCost"),
                JsonValues.Bool(root, "isVip"),
                JsonValues.Bool(root, "isRenewal")
            );
        }
        catch
        {
            // Intentionally ignore malformed payloads.
            return null;
        }
    }
}

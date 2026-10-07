using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Orleans;
using Vortex.Dashboard.API.Api.Platform.Contracts;
using Vortex.Database.Context;
using Vortex.Observability.Metrics;
using Vortex.Observability.Runtime;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Rooms.Grains;

namespace Vortex.Dashboard.API.Api.Platform;

/// <summary>
/// The health, incident and live-traffic reads behind the overview. Split out of
/// <c>DashboardApiService</c> — now gone — because it was the reason that class had fourteen constructor
/// parameters: six of them -- the live stats aggregator, incident detection, infrastructure health,
/// club metrics, client performance metrics and the meter -- were read here and nowhere else, so
/// every one of the thirty other read partials carried them for nothing.
///
/// <para>
/// The row-count cache comes with it. Those COUNT(*)s are full scans of tables that grow without
/// bound, cached for half a minute because the overview polls; concurrent misses simply recompute
/// the same value, so no lock is needed.
/// </para>
/// </summary>
internal sealed class DashboardMonitoringReads(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IGrainFactory grainFactory,
    ISessionGateway sessionGateway,
    ILiveStatsAggregator liveStats,
    IIncidentDetectionService incidentDetection,
    IInfrastructureHealthService infrastructureHealth,
    MembershipMetrics membershipMetrics,
    ClientPerformanceMetrics clientPerformanceMetrics,
    IVortexMetrics metrics,
    RoomPerformanceAggregator roomPerformance
)
{
    private static readonly TimeSpan TotalsCacheTtl = TimeSpan.FromSeconds(30);

    private readonly IDbContextFactory<VortexDbContext> _dbContextFactory = dbContextFactory;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly ISessionGateway _sessionGateway = sessionGateway;
    private readonly ILiveStatsAggregator _liveStats = liveStats;
    private readonly IIncidentDetectionService _incidentDetection = incidentDetection;
    private readonly IInfrastructureHealthService _infrastructureHealth = infrastructureHealth;
    private readonly MembershipMetrics _membershipMetrics = membershipMetrics;
    private readonly ClientPerformanceMetrics _clientPerformanceMetrics = clientPerformanceMetrics;
    private readonly IVortexMetrics _metrics = metrics;
    private readonly RoomPerformanceAggregator _roomPerformance = roomPerformance;

    private volatile CachedTotals? _cachedTotals;

    public async Task<PacketStats> PacketStatsAsync(CancellationToken ct)
    {
        LiveStatsSnapshot live = await _liveStats.GetSnapshotAsync().ConfigureAwait(false);

        return new PacketStats(
            Math.Round(live.PacketsPerSecond, 2),
            Math.Round(live.ErrorsPerMinute, 2),
            Math.Round(live.LatencyP50Ms, 2),
            Math.Round(live.LatencyP95Ms, 2),
            Rounded(live.TopOperations),
            Rounded(live.TopFailedOperations)
        );
    }

    /// <summary>The same rounding the four rates above get, applied to a breakdown.</summary>
    private static List<LivePacketOperationSnapshot> Rounded(
        IReadOnlyList<LivePacketOperationSnapshot> operations
    ) =>
        [
            .. operations.Select(o => new LivePacketOperationSnapshot(
                o.Operation,
                Math.Round(o.PacketsPerMinute, 2)
            )),
        ];

    /// <summary>
    /// Room tick and room-directory latency over the live stats window. Synchronous and lock-only —
    /// the samples are already in memory, read off the same meter the Prometheus endpoint exports, so
    /// there is nothing to await and no database involved.
    /// </summary>
    public RoomPerformanceSnapshot RoomPerformance() => _roomPerformance.GetSnapshot();

    public Task<InfrastructureHealthSnapshot> InfrastructureAsync(CancellationToken ct) =>
        _infrastructureHealth.GetStatusAsync(ct);

    public async Task<DashboardOverview> OverviewAsync(DateTime startedAtUtc, CancellationToken ct)
    {
        VortexDbContext db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        try
        {
            InfrastructureHealthSnapshot health = await _infrastructureHealth
                .GetStatusAsync(ct)
                .ConfigureAwait(false);
            IncidentDetectionSnapshot incidents = await _incidentDetection
                .DetectAsync(ct)
                .ConfigureAwait(false);
            LiveStatsSnapshot live = await _liveStats.GetSnapshotAsync().ConfigureAwait(false);
            ImmutableArray<RoomSummarySnapshot> activeRooms;

            using (
                _metrics.MeasureRoomDirectoryCall(nameof(IRoomDirectoryGrain.GetActiveRoomsAsync))
            )
            {
                activeRooms = await _grainFactory
                    .GetRoomDirectoryGrain()
                    .GetActiveRoomsAsync()
                    .ConfigureAwait(false);
            }

            DateTime since = DateTime.UtcNow.AddHours(-1);

            List<AuditCategoryCount> byCategory = await db
                .AuditEvents.AsNoTracking()
                .Where(a => a.OccurredAt >= since)
                .GroupBy(a => a.Category)
                .Select(g => new AuditCategoryCount(g.Key.ToString(), g.Count()))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            CachedTotals totals = await GetTotalsAsync(db, ct).ConfigureAwait(false);

            return new DashboardOverview(
                health.Overall,
                health,
                (long)(DateTime.UtcNow - startedAtUtc).TotalSeconds,
                GC.GetTotalMemory(false) / 1024 / 1024,
                _sessionGateway.GetActiveSessionCount(),
                activeRooms.Length,
                _membershipMetrics.ActiveSubscribers,
                incidents,
                new OverviewLive(
                    Math.Round(live.PacketsPerSecond, 2),
                    Math.Round(live.ErrorsPerMinute, 2),
                    Math.Round(live.LatencyP50Ms, 2),
                    Math.Round(live.LatencyP95Ms, 2),
                    live.TopAbusers,
                    live.TopRooms
                ),
                byCategory,
                new OverviewTotals(
                    totals.Audit,
                    totals.Ledger,
                    totals.Items,
                    _clientPerformanceMetrics.TotalSamples,
                    totals.AtUtc
                )
            );
        }
        finally
        {
            await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    public Task<IncidentDetectionSnapshot> IncidentsAsync(CancellationToken ct) =>
        _incidentDetection.DetectAsync(ct);

    /// <summary>
    /// Row-count totals are full-table scans on tables that grow without bound, so they are cached
    /// for a short interval instead of being recomputed on every overview poll. Concurrent cache
    /// misses simply recompute the same value, so no lock is needed.
    /// </summary>
    private async Task<CachedTotals> GetTotalsAsync(VortexDbContext db, CancellationToken ct)
    {
        CachedTotals? cached = _cachedTotals;

        if (cached is not null && DateTime.UtcNow - cached.AtUtc < TotalsCacheTtl)
        {
            return cached;
        }

        CachedTotals fresh = new CachedTotals(
            DateTime.UtcNow,
            await db.AuditEvents.CountAsync(ct).ConfigureAwait(false),
            await db.EconomyLedger.CountAsync(ct).ConfigureAwait(false),
            await db.ItemEvents.CountAsync(ct).ConfigureAwait(false)
        );

        _cachedTotals = fresh;

        return fresh;
    }

    private sealed record CachedTotals(DateTime AtUtc, long Audit, long Ledger, long Items);
}

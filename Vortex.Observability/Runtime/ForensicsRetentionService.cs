using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Observability.Configuration;

namespace Vortex.Observability.Runtime;

/// <summary>
/// Ages out the forensic tables. Every player action now leaves a row, so these grow with traffic
/// rather than with the size of the hotel, and nothing was ever deleting them.
/// </summary>
/// <remarks>
/// Deletes in bounded batches rather than one statement per table: a single unqualified DELETE over
/// months of history locks the table for as long as it takes, and the tables it walks are the ones
/// the moderation tooling reads live.
/// <para>
/// Retention is per-table because the tables answer different questions. Chat and room visits are
/// the privacy-bearing ones and expire soonest; the audit trail and item provenance are what a
/// dispute is settled with months later, so they keep longer. A retention of 0 disables ageing for
/// that table, which is the default for item provenance -- an item's history is the item.
/// </para>
/// </remarks>
public sealed class ForensicsRetentionService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IOptions<ObservabilityConfig> config,
    ILogger<ForensicsRetentionService> logger
) : BackgroundService
{
    private readonly ObservabilityConfig _config = config.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromHours(Math.Max(1, _config.RetentionSweepIntervalHours));

        // Nothing to do on a hotel that keeps everything; do not wake up every hour to prove it.
        if (
            _config.AuditRetentionDays <= 0
            && _config.RoomVisitRetentionDays <= 0
            && _config.ItemEventRetentionDays <= 0
        )
        {
            logger.LogInformation(
                "Forensics retention is disabled for every table; the sweep will not run."
            );

            return;
        }

        // A first sweep at startup would land in the middle of the login rush a restart causes.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Forensics retention sweep failed; will retry next cycle.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One sweep, exposed so a test can drive it without waiting out the start delay.</summary>
    internal Task SweepOnceAsync(CancellationToken ct) => SweepAsync(ct);

    private async Task SweepAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        DateTime now = DateTime.UtcNow;
        int batchSize = Math.Max(100, _config.RetentionBatchSize);

        await DeleteOlderThanAsync(
                "audit_events",
                _config.AuditRetentionDays,
                cutoff =>
                    dbCtx.AuditEvents.Where(a => a.OccurredAt < cutoff).OrderBy(a => a.OccurredAt),
                now,
                batchSize,
                ct
            )
            .ConfigureAwait(false);

        await DeleteOlderThanAsync(
                "item_events",
                _config.ItemEventRetentionDays,
                cutoff =>
                    dbCtx.ItemEvents.Where(i => i.OccurredAt < cutoff).OrderBy(i => i.OccurredAt),
                now,
                batchSize,
                ct
            )
            .ConfigureAwait(false);

        await DeleteOlderThanAsync(
                "room_entry_logs",
                _config.RoomVisitRetentionDays,
                cutoff =>
                    dbCtx.RoomEntryLogs.Where(e => e.CreatedAt < cutoff).OrderBy(e => e.CreatedAt),
                now,
                batchSize,
                ct
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes one table's expired rows in batches, oldest first, stopping when a batch comes back
    /// short. The per-cycle cap exists so a hotel switching retention on for the first time pays for
    /// its backlog over several cycles instead of in one statement.
    /// </summary>
    private async Task DeleteOlderThanAsync<T>(
        string table,
        int retentionDays,
        Func<DateTime, IOrderedQueryable<T>> expired,
        DateTime now,
        int batchSize,
        CancellationToken ct
    )
        where T : class
    {
        if (retentionDays <= 0)
        {
            return;
        }

        DateTime cutoff = now.AddDays(-retentionDays);
        int deleted = 0;
        int maxPerCycle = Math.Max(batchSize, _config.RetentionMaxRowsPerCycle);

        while (deleted < maxPerCycle && !ct.IsCancellationRequested)
        {
            int batch = await expired(cutoff)
                .Take(batchSize)
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);

            deleted += batch;

            if (batch < batchSize)
            {
                break;
            }
        }

        if (deleted > 0)
        {
            logger.LogInformation(
                "Forensics retention: removed {Count} row(s) from {Table} older than {Cutoff:u}.",
                deleted,
                table,
                cutoff
            );
        }
    }
}

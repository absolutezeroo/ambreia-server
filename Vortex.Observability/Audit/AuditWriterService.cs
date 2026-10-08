using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Database.Entities.Audit;
using Vortex.Observability.Configuration;
using Vortex.Observability.Diagnostics;
using Vortex.Observability.Persistence;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Audit;

/// <summary>
///     Single background consumer of the durable-observability channel. It batches records of every family
///     (audit, item forensics), routes each to its table, and persists them with one
///     <c>SaveChanges</c> per batch — keeping all durable writes off the gameplay hot path. A failed
///     flush retries a bounded number of times and then writes to a dead-letter file to avoid losing audit
///     history (<see cref="ChannelBatchWriter{TRecord}"/>).
/// </summary>
internal sealed class AuditWriterService(
    AuditChannel channel,
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IOptions<ObservabilityConfig> options,
    IVortexMetrics metrics,
    ILogger<AuditWriterService> logger
)
    : ChannelBatchWriter<DurableRecord>(
        channel.Reader,
        options.Value.AuditBatchSize,
        options.Value.AuditWriteRetryAttempts,
        options.Value.AuditWriteRetryDelayMs,
        options.Value.AuditDeadLetterPath,
        new BatchWriterEvents(
            "durable observability record(s)",
            VortexEventIds.AuditWriteRetry,
            VortexEventIds.AuditWriteFailed,
            VortexEventIds.AuditWriteDeadLettered,
            VortexEventIds.AuditDeadLetterWriteFailed
        ),
        logger
    )
{
    protected override async Task PersistAsync(List<DurableRecord> batch, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        foreach (DurableRecord record in batch)
        {
            db.Add(MapEntity(record));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    protected override object MapDeadLetterPayload(DurableRecord record) => DeadLetterRow(record);

    protected override void OnBatchLost() => metrics.AuditWriteFailed("persist");

    // Internal for DurableRecordDeadLetterTests (Vortex.Database.Tests): a record type added to
    // MapEntity and not here loses the WHOLE batch it sits in exactly when the database is down.
    internal static object DeadLetterRow(DurableRecord record)
    {
        return record switch
        {
            AuditRecord audit => new
            {
                kind = nameof(AuditRecord),
                occurredAt = audit.OccurredAt,
                correlationId = audit.CorrelationId,
                audit = audit.Event,
            },
            ItemRecord item => new
            {
                kind = nameof(ItemRecord),
                occurredAt = item.OccurredAt,
                correlationId = item.CorrelationId,
                item = item.Event,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(record), record.GetType().Name, null),
        };
    }

    private static object MapEntity(DurableRecord record)
    {
        return record switch
        {
            AuditRecord audit => MapAudit(audit),
            ItemRecord item => MapItem(item),
            _ => throw new ArgumentOutOfRangeException(
                nameof(record),
                record.GetType().Name,
                "Unknown durable observability record type."
            ),
        };
    }

    private static AuditEventEntity MapAudit(AuditRecord record)
    {
        return new AuditEventEntity
        {
            OccurredAt = record.OccurredAt,
            Category = record.Event.Category,
            Action = record.Event.Action,
            Severity = record.Event.Severity,
            Result = record.Event.Result,
            CorrelationId = record.CorrelationId,
            ActorPlayerId = record.Event.ActorPlayerId,
            TargetPlayerId = record.Event.TargetPlayerId,
            RoomId = record.Event.RoomId,
            ItemId = record.Event.ItemId,
            IpHash = record.Event.IpHash,
            Amount = record.Event.Amount,
            Quantity = record.Event.Quantity,
            Data = record.Event.Data,
        };
    }

    private static ItemEventEntity MapItem(ItemRecord record)
    {
        return new ItemEventEntity
        {
            OccurredAt = record.OccurredAt,
            ItemId = record.Event.ItemId,
            EventType = record.Event.EventType,
            ActorPlayerId = record.Event.ActorPlayerId,
            FromOwnerId = record.Event.FromOwnerId,
            ToOwnerId = record.Event.ToOwnerId,
            RoomId = record.Event.RoomId,
            CorrelationId = record.CorrelationId,
            Data = record.Event.Data,
        };
    }
}

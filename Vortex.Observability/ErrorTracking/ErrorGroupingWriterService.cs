using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Database.Entities.Errors;
using Vortex.Observability.Configuration;
using Vortex.Observability.Diagnostics;
using Vortex.Observability.Persistence;

namespace Vortex.Observability.ErrorTracking;

/// <summary>
///     Background worker that flushes grouped technical errors from an in-memory channel to
///     <c>error_groups</c> and <c>error_occurrences</c> with bounded retry and dead-lettering
///     (<see cref="ChannelBatchWriter{TRecord}"/>).
/// </summary>
internal sealed class ErrorGroupingWriterService(
    ErrorGroupingChannel channel,
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IOptions<ObservabilityConfig> options,
    ILogger<ErrorGroupingWriterService> logger
)
    : ChannelBatchWriter<ErrorGroupingRecord>(
        channel.Reader,
        options.Value.ErrorTrackingBatchSize,
        options.Value.ErrorTrackingWriteRetryAttempts,
        options.Value.ErrorTrackingWriteRetryDelayMs,
        options.Value.ErrorGroupingDeadLetterPath,
        new BatchWriterEvents(
            "error-grouping rows",
            VortexEventIds.ErrorGroupingWriteRetry,
            VortexEventIds.ErrorGroupingWriteFailed,
            VortexEventIds.ErrorGroupingWriteDeadLettered,
            VortexEventIds.ErrorGroupingDeadLetterWriteFailed
        ),
        logger
    )
{
    protected override async Task PersistAsync(
        List<ErrorGroupingRecord> batch,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        Dictionary<string, ErrorGroupEntity> groupsByFingerprint = await LoadOrCreateGroupsAsync(
                db,
                batch,
                ct
            )
            .ConfigureAwait(false);

        // Persist new groups first so their auto-generated IDs are available
        // before ErrorOccurrenceEntity.GroupId is set.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        foreach (ErrorGroupingRecord record in batch)
        {
            if (!groupsByFingerprint.TryGetValue(record.Fingerprint, out ErrorGroupEntity? group))
            {
                continue;
            }

            group.TotalOccurrences++;
            group.LastSeenAt = record.OccurredAt;
            group.LastActorPlayerId = record.ActorPlayerId;
            group.LastRoomId = record.RoomId;
            group.LastCorrelationId = record.CorrelationId;
            if (string.IsNullOrWhiteSpace(group.SampleMessage))
            {
                group.SampleMessage = Truncate(record.Message, 255);
            }

            if (string.IsNullOrWhiteSpace(group.MessageSignature))
            {
                group.MessageSignature = record.MessageSignature;
            }

            if (group.FirstSeenAt == default)
            {
                group.FirstSeenAt = record.OccurredAt;
            }

            db.ErrorOccurrences.Add(
                new ErrorOccurrenceEntity
                {
                    Fingerprint = group.Fingerprint,
                    GroupId = group.Id,
                    OccurredAt = record.OccurredAt,
                    Source = record.Source,
                    Operation = record.Operation,
                    ExceptionType = record.ExceptionType,
                    Message = record.Message,
                    StackTrace = record.StackTrace,
                    CorrelationId = record.CorrelationId,
                    ActorPlayerId = record.ActorPlayerId,
                    RoomId = record.RoomId,
                    SessionKey = record.SessionKey,
                    RemoteIp = record.RemoteIp,
                }
            );
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    protected override object MapDeadLetterPayload(ErrorGroupingRecord record)
    {
        return new
        {
            source = record.Source,
            operation = record.Operation,
            fingerprint = record.Fingerprint,
            exceptionType = record.ExceptionType,
            message = record.Message,
            occurredAt = record.OccurredAt.ToString("O", CultureInfo.InvariantCulture),
            actorPlayerId = record.ActorPlayerId,
            roomId = record.RoomId,
            correlationId = record.CorrelationId,
        };
    }

    private static async Task<Dictionary<string, ErrorGroupEntity>> LoadOrCreateGroupsAsync(
        VortexDbContext db,
        List<ErrorGroupingRecord> batch,
        CancellationToken ct
    )
    {
        Dictionary<string, ErrorGroupEntity> byFingerprint = batch
            .GroupBy(item => item.Fingerprint)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    ErrorGroupingRecord first = group.First();

                    return new ErrorGroupEntity
                    {
                        Fingerprint = first.Fingerprint,
                        Source = first.Source,
                        Operation = first.Operation,
                        ExceptionType = first.ExceptionType,
                        MessageSignature = first.MessageSignature,
                        SampleMessage = Truncate(first.Message, 255),
                        FirstSeenAt = first.OccurredAt,
                        LastSeenAt = first.OccurredAt,
                        TotalOccurrences = 0,
                    };
                },
                StringComparer.Ordinal
            );

        Dictionary<string, ErrorGroupEntity> existing = await db
            .ErrorGroups.Where(group => byFingerprint.Keys.Contains(group.Fingerprint))
            .ToDictionaryAsync(group => group.Fingerprint, ct)
            .ConfigureAwait(false);

        foreach (KeyValuePair<string, ErrorGroupEntity> existingPair in existing)
        {
            byFingerprint[existingPair.Key] = existingPair.Value;
        }

        List<ErrorGroupEntity> toInsert = byFingerprint
            .Values.Where(group => group.Id == 0)
            .ToList();

        if (toInsert.Count > 0)
        {
            await db.ErrorGroups.AddRangeAsync(toInsert, ct).ConfigureAwait(false);
        }

        return byFingerprint;
    }

    private static string Truncate(string? s, int maxLength)
    {
        return string.IsNullOrEmpty(s) ? string.Empty
            : s.Length <= maxLength ? s
            : s[..maxLength];
    }
}

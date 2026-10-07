using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Database.Entities.Errors;
using Vortex.Observability.Configuration;
using Vortex.Observability.Diagnostics;

namespace Vortex.Observability.ErrorTracking;

/// <summary>
///     Background worker that flushes grouped technical errors from an in-memory channel to
///     <c>error_groups</c> and <c>error_occurrences</c> with bounded retry and dead-lettering.
/// </summary>
internal sealed class ErrorGroupingWriterService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly int _batchSize;

    private readonly ErrorGroupingChannel _channel;
    private readonly IDbContextFactory<VortexDbContext> _dbContextFactory;
    private readonly string _deadLetterPath;
    private readonly ILogger<ErrorGroupingWriterService> _logger;
    private readonly int _retryAttempts;
    private readonly int _retryDelayMs;

    public ErrorGroupingWriterService(
        ErrorGroupingChannel channel,
        IDbContextFactory<VortexDbContext> dbContextFactory,
        IOptions<ObservabilityConfig> options,
        ILogger<ErrorGroupingWriterService> logger
    )
    {
        _channel = channel;
        _dbContextFactory = dbContextFactory;
        _batchSize = Math.Max(1, options.Value.ErrorTrackingBatchSize);
        _retryAttempts = Math.Max(0, options.Value.ErrorTrackingWriteRetryAttempts);
        _retryDelayMs = Math.Max(0, options.Value.ErrorTrackingWriteRetryDelayMs);
        _deadLetterPath = options.Value.ErrorGroupingDeadLetterPath;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ChannelReader<ErrorGroupingRecord> reader = _channel.Reader;
        List<ErrorGroupingRecord> batch = new(_batchSize);

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                while (batch.Count < _batchSize && reader.TryRead(out ErrorGroupingRecord? record))
                {
                    batch.Add(record);
                }

                if (batch.Count > 0)
                {
                    await FlushAsync(batch, stoppingToken).ConfigureAwait(false);
                    batch.Clear();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown. Drain any remaining records below.
        }

        await DrainRemainingAsync(reader, batch).ConfigureAwait(false);
    }

    private async Task FlushAsync(List<ErrorGroupingRecord> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
        {
            return;
        }

        if (await TryPersistBatchWithRetryAsync(batch, ct).ConfigureAwait(false))
        {
            return;
        }

        await WriteDeadLetterAsync(batch).ConfigureAwait(false);
    }

    private async Task DrainRemainingAsync(
        ChannelReader<ErrorGroupingRecord> reader,
        List<ErrorGroupingRecord> batch
    )
    {
        // Not cleared: a stop that cancels a flush mid-write leaves that batch here, and it must
        // still reach the database or the dead-letter file.
        while (reader.TryRead(out ErrorGroupingRecord? record))
        {
            batch.Add(record);
        }

        if (batch.Count == 0)
        {
            return;
        }

        bool persisted = await TryPersistBatchWithRetryAsync(batch, CancellationToken.None)
            .ConfigureAwait(false);

        if (persisted)
        {
            return;
        }

        await WriteDeadLetterAsync(batch).ConfigureAwait(false);
    }

    private async Task<bool> TryPersistBatchWithRetryAsync(
        List<ErrorGroupingRecord> batch,
        CancellationToken ct
    )
    {
        for (int attempt = 0; attempt <= _retryAttempts; attempt++)
        {
            try
            {
                await using VortexDbContext db = await _dbContextFactory
                    .CreateDbContextAsync(ct)
                    .ConfigureAwait(false);

                Dictionary<string, ErrorGroupEntity> groupsByFingerprint =
                    await LoadOrCreateGroupsAsync(db, batch, ct).ConfigureAwait(false);

                // Persist new groups first so their auto-generated IDs are available
                // before ErrorOccurrenceEntity.GroupId is set.
                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                foreach (ErrorGroupingRecord record in batch)
                {
                    if (
                        !groupsByFingerprint.TryGetValue(
                            record.Fingerprint,
                            out ErrorGroupEntity? group
                        )
                    )
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
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _retryAttempts)
            {
                _logger.LogWarning(
                    VortexEventIds.ErrorGroupingWriteRetry,
                    ex,
                    "Failed to persist a batch of {Count} error-grouping rows (attempt {Attempt}/{Max}). Retrying.",
                    batch.Count,
                    attempt + 1,
                    _retryAttempts
                );

                if (_retryDelayMs > 0)
                {
                    await Task.Delay(_retryDelayMs, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    VortexEventIds.ErrorGroupingWriteFailed,
                    ex,
                    "Failed to persist a batch of {Count} error-grouping rows; moving to dead-letter.",
                    batch.Count
                );

                return false;
            }
        }

        return false;
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

    private async Task WriteDeadLetterAsync(List<ErrorGroupingRecord> batch)
    {
        if (string.IsNullOrWhiteSpace(_deadLetterPath))
        {
            return;
        }

        try
        {
            string path = Path.IsPathRooted(_deadLetterPath)
                ? _deadLetterPath
                : Path.Combine(AppContext.BaseDirectory, _deadLetterPath);

            string? directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string payload = JsonSerializer.Serialize(
                new
                {
                    happenedAtUtc = DateTime.UtcNow,
                    records = batch.Select(MapDeadLetterPayload),
                },
                JsonOptions
            );

            await File.AppendAllTextAsync(path, $"{payload}{Environment.NewLine}", Encoding.UTF8)
                .ConfigureAwait(false);

            _logger.LogInformation(
                VortexEventIds.ErrorGroupingWriteDeadLettered,
                "Wrote {Count} error-grouping records to dead-letter file {Path}",
                batch.Count,
                path
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                VortexEventIds.ErrorGroupingDeadLetterWriteFailed,
                ex,
                "Failed to write {Count} error-grouping records to dead-letter file.",
                batch.Count
            );
        }
    }

    private static string Truncate(string? s, int maxLength)
    {
        return string.IsNullOrEmpty(s) ? string.Empty
            : s.Length <= maxLength ? s
            : s[..maxLength];
    }

    private static object MapDeadLetterPayload(ErrorGroupingRecord record)
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
}

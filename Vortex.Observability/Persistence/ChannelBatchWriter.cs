using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vortex.Observability.Persistence;

/// <summary>
/// The single background consumer of one observability channel: it batches what the game emitted,
/// persists each batch off the gameplay hot path, retries a bounded number of times, and writes a
/// batch it could not persist to a dead-letter file rather than losing it. On stop it drains what
/// is left, including a batch whose flush the stop cancelled.
/// </summary>
/// <remarks>
/// A subclass says only what is its own: how a batch reaches its tables
/// (<see cref="PersistAsync"/>) and what one row looks like in the dead-letter file
/// (<see cref="MapDeadLetterPayload"/>). The loop, the drain and the retry were written twice before
/// this, and fixed twice.
/// </remarks>
public abstract class ChannelBatchWriter<TRecord> : BackgroundService
    where TRecord : class
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ChannelReader<TRecord> _reader;
    private readonly int _batchSize;
    private readonly int _retryAttempts;
    private readonly int _retryDelayMs;
    private readonly string _deadLetterPath;
    private readonly BatchWriterEvents _events;
    private readonly ILogger _logger;

    /// <param name="deadLetterPath">Relative to the application directory unless rooted; empty for
    /// none.</param>
    protected ChannelBatchWriter(
        ChannelReader<TRecord> reader,
        int batchSize,
        int retryAttempts,
        int retryDelayMs,
        string deadLetterPath,
        BatchWriterEvents events,
        ILogger logger
    )
    {
        _reader = reader;
        _batchSize = Math.Max(1, batchSize);
        _retryAttempts = Math.Max(0, retryAttempts);
        _retryDelayMs = Math.Max(0, retryDelayMs);
        _deadLetterPath =
            string.IsNullOrWhiteSpace(deadLetterPath) ? string.Empty
            : Path.IsPathRooted(deadLetterPath) ? deadLetterPath
            : Path.Combine(AppContext.BaseDirectory, deadLetterPath);
        _events = events;
        _logger = logger;
    }

    /// <summary>Writes one batch, in one go; throws to have it retried.</summary>
    protected abstract Task PersistAsync(List<TRecord> batch, CancellationToken ct);

    /// <summary>One row as the dead-letter file keeps it.</summary>
    protected abstract object MapDeadLetterPayload(TRecord record);

    /// <summary>A batch is about to be given up on: it is not in the tables, wherever else it goes.</summary>
    protected virtual void OnBatchLost() { }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        List<TRecord> batch = new(_batchSize);

        try
        {
            while (await _reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                while (batch.Count < _batchSize && _reader.TryRead(out TRecord? record))
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
            // Shutdown requested; fall through to a best-effort final drain.
        }

        // Not cleared: a stop that cancels a flush mid-write leaves that batch here, and it must
        // still reach the database or the dead-letter file.
        while (_reader.TryRead(out TRecord? record))
        {
            batch.Add(record);
        }

        await FlushAsync(batch, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task FlushAsync(List<TRecord> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
        {
            return;
        }

        if (await TryPersistWithRetryAsync(batch, ct).ConfigureAwait(false))
        {
            return;
        }

        await WriteDeadLetterAsync(batch).ConfigureAwait(false);
    }

    private async Task<bool> TryPersistWithRetryAsync(List<TRecord> batch, CancellationToken ct)
    {
        for (int attempt = 0; attempt <= _retryAttempts; attempt++)
        {
            try
            {
                await PersistAsync(batch, ct).ConfigureAwait(false);

                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _retryAttempts)
            {
                _logger.LogWarning(
                    _events.Retry,
                    ex,
                    "Failed to persist a batch of {Count} {Kind} (attempt {Attempt}/{Max}). Retrying.",
                    batch.Count,
                    _events.Kind,
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
                    _events.Failed,
                    ex,
                    "Failed to persist a batch of {Count} {Kind}; moving to dead-letter.",
                    batch.Count,
                    _events.Kind
                );

                return false;
            }
        }

        return false;
    }

    private async Task WriteDeadLetterAsync(List<TRecord> batch)
    {
        // Before the path check: reaching this at all means the batch is not in the tables, and
        // with no dead-letter path it is not anywhere else either.
        OnBatchLost();

        if (_deadLetterPath.Length == 0)
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(_deadLetterPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string payload = JsonSerializer.Serialize(
                new
                {
                    happenedAtUtc = DateTime.UtcNow,
                    records = batch.Select(MapDeadLetterPayload).ToList(),
                },
                JsonOptions
            );

            await File.AppendAllTextAsync(
                    _deadLetterPath,
                    $"{payload}{Environment.NewLine}",
                    Encoding.UTF8
                )
                .ConfigureAwait(false);

            _logger.LogInformation(
                _events.DeadLettered,
                "Wrote {Count} {Kind} to dead-letter file {Path}",
                batch.Count,
                _events.Kind,
                _deadLetterPath
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                _events.DeadLetterFailed,
                ex,
                "Failed to write {Count} {Kind} to dead-letter file {Path}.",
                batch.Count,
                _events.Kind,
                _deadLetterPath
            );
        }
    }
}

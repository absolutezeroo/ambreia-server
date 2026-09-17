using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Database.Configuration;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;

namespace Vortex.Database.Commerce;

/// <summary>
/// Publishes the critical events that completed operations still owe, and escalates operations that
/// have been stuck past their pivot for too long.
/// </summary>
/// <remarks>
/// <para>
/// The relay half is the outbox: an operation writes its event with its terminal transition, and
/// this publishes it afterwards. Under normal conditions the flow publishes its own event and marks
/// it relayed within milliseconds, and this finds nothing. It exists for the crash in between, which
/// used to lose the event outright — and with it the quest progress and the daily task it feeds.
/// Delivery is at-least-once, which is why the consumers that change player state deduplicate by
/// operation id.
/// </para>
/// <para>
/// The escalation half does not repair anything. Resuming a half-delivered purchase needs per-flow
/// knowledge this does not have; what it does is stop an operation from sitting in
/// <see cref="CommerceOperationState.Pivoted"/> forever without anyone knowing, which was the state
/// of every commerce failure in this codebase until now.
/// </para>
/// </remarks>
public sealed class CommerceRelayService(
    ICommerceJournal journal,
    IEventPublisher events,
    IOptions<CommerceRecoveryConfig> config,
    ILogger<CommerceRelayService> logger
) : BackgroundService
{
    private readonly ICommerceJournal _journal = journal;
    private readonly IEventPublisher _events = events;
    private readonly CommerceRecoveryConfig _config = config.Value;
    private readonly ILogger<CommerceRelayService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(_config.SweepIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await RelayAsync(stoppingToken).ConfigureAwait(false);
                await EscalateAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A sweep that throws must not take the loop with it: the next one is the retry.
                _logger.LogError(ex, "The commerce relay sweep failed; retrying next tick.");
            }
        }
    }

    internal async Task RelayAsync(CancellationToken ct)
    {
        IReadOnlyList<CommerceRelayEntry> owed = await _journal
            .GetUnrelayedAsync(_config.RelayBatchSize, ct)
            .ConfigureAwait(false);

        foreach (CommerceRelayEntry entry in owed)
        {
            IEvent? rebuilt = Rebuild(entry);

            if (rebuilt is null)
            {
                // An event type that no longer exists is not going to start existing. Marking it
                // relayed is the only way it stops being retried every sweep forever.
                _logger.LogError(
                    "Commerce operation {OperationId} owes a {TypeName}, which this build does not "
                        + "know how to rebuild; dropping it.",
                    entry.OperationId,
                    entry.TypeName
                );

                await _journal.MarkRelayedAsync(entry.OperationId, ct).ConfigureAwait(false);

                continue;
            }

            await _events.PublishAsync(rebuilt, ct).ConfigureAwait(false);
            await _journal.MarkRelayedAsync(entry.OperationId, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Relayed {TypeName} for commerce operation {OperationId}.",
                entry.TypeName,
                entry.OperationId
            );
        }
    }

    internal async Task EscalateAsync(CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow.AddMinutes(-_config.StuckAfterMinutes);

        // Asking for what can actually be escalated, rather than for everything incomplete and then
        // skipping most of it. The skipping happened after the batch limit had been applied, so a
        // backlog of old interventions filled the batch and starved every newer stuck operation.
        IReadOnlyList<CommerceOperationRecord> stuck = await _journal
            .GetEscalatableAsync(deadline, _config.RelayBatchSize, ct)
            .ConfigureAwait(false);

        foreach (CommerceOperationRecord record in stuck)
        {
            _logger.LogCritical(
                "Commerce operation {OperationId} ({Kind}, player {PlayerId}) has been past its "
                    + "pivot since {PivotedAt} on step {Step}: {Detail}. Last error: {Error}.",
                record.Id,
                record.Kind,
                record.PlayerId,
                record.PivotedAt,
                record.CurrentStep,
                record.Detail,
                record.LastError
            );

            await _journal
                .TransitionAsync(
                    record.Id,
                    CommerceOperationState.NeedsIntervention,
                    record.CurrentStep,
                    "stuck past its pivot",
                    ct
                )
                .ConfigureAwait(false);
        }
    }

    private static readonly ConcurrentDictionary<string, Type?> KnownEvents = new();

    /// <summary>
    /// Rebuilds a stored event from its short type name. Short rather than assembly-qualified on
    /// purpose: a stored name has to survive the assembly being renamed or the type moving namespace,
    /// and the event types are unique by name across the contracts assembly.
    /// </summary>
    private static IEvent? Rebuild(CommerceRelayEntry entry)
    {
        Type? type = KnownEvents.GetOrAdd(
            entry.TypeName,
            static name =>
                typeof(IEvent)
                    .Assembly.GetTypes()
                    .FirstOrDefault(t =>
                        t.Name == name && typeof(IEvent).IsAssignableFrom(t) && !t.IsAbstract
                    )
        );

        if (type is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(entry.Payload, type) as IEvent;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

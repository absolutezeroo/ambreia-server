using System;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Audit;

/// <summary>
/// Base transport item on the durable-observability channel: the timestamp and correlation id captured
/// at emit time, so persisted records reflect when the action happened, not when the writer flushed.
/// Concrete records wrap one durable event family (audit, item forensics).
/// </summary>
internal abstract record DurableRecord(DateTime OccurredAt, string? CorrelationId);

internal sealed record AuditRecord(AuditEvent Event, DateTime OccurredAt, string? CorrelationId)
    : DurableRecord(OccurredAt, CorrelationId);

internal sealed record ItemRecord(
    ItemForensicEvent Event,
    DateTime OccurredAt,
    string? CorrelationId
) : DurableRecord(OccurredAt, CorrelationId);

using System;

namespace Vortex.Observability.Audit;

/// <summary>
/// Base transport item on the durable-observability channel: the timestamp and correlation id captured
/// at emit time, so persisted records reflect when the action happened, not when the writer flushed.
/// Concrete records wrap one durable event family (<see cref="AuditRecord" />,
/// <see cref="ItemRecord" />).
/// </summary>
internal abstract record DurableRecord(DateTime OccurredAt, string? CorrelationId);

using System;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Audit;

internal sealed record AuditRecord(AuditEvent Event, DateTime OccurredAt, string? CorrelationId)
    : DurableRecord(OccurredAt, CorrelationId);

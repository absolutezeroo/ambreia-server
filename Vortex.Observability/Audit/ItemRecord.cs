using System;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Audit;

internal sealed record ItemRecord(
    ItemForensicEvent Event,
    DateTime OccurredAt,
    string? CorrelationId
) : DurableRecord(OccurredAt, CorrelationId);

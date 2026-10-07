using Microsoft.Extensions.Logging;

namespace Vortex.Observability.Diagnostics;

/// <summary>
/// Centrally-allocated, stable <see cref="EventId"/>s. Ranges are reserved per concern so ids never
/// collide and can be referenced from log queries:
/// <list type="bullet">
/// <item>1000-1099 observability core</item>
/// <item>1200-1299 audit pipeline</item>
/// <item>1400-1499 error grouping</item>
/// </list>
/// New ids are appended here rather than declared inline at the call site. A removed id leaves its
/// number unused, so an old log query never matches a different event.
/// </summary>
public static class VortexEventIds
{
    // 1000 ObservabilityReady and 1001 TraceScopeFault: retired, never logged.
    public static readonly EventId GrainContextFault = new(1002, nameof(GrainContextFault));

    public static readonly EventId AuditDropped = new(1200, nameof(AuditDropped));
    public static readonly EventId AuditWriteFailed = new(1201, nameof(AuditWriteFailed));
    public static readonly EventId AuditWriteRetry = new(1202, nameof(AuditWriteRetry));
    public static readonly EventId AuditWriteDeadLettered = new(
        1203,
        nameof(AuditWriteDeadLettered)
    );
    public static readonly EventId AuditDeadLetterWriteFailed = new(
        1204,
        nameof(AuditDeadLetterWriteFailed)
    );

    // 1210-1213: retired with the word filter (cut of 2026-10-07).

    // 1400 HandlerPipelineError: retired, never logged.
    public static readonly EventId ErrorGroupingDropped = new(1401, nameof(ErrorGroupingDropped));
    public static readonly EventId ErrorGroupingWriteRetry = new(
        1402,
        nameof(ErrorGroupingWriteRetry)
    );
    public static readonly EventId ErrorGroupingWriteFailed = new(
        1403,
        nameof(ErrorGroupingWriteFailed)
    );
    public static readonly EventId ErrorGroupingWriteDeadLettered = new(
        1404,
        nameof(ErrorGroupingWriteDeadLettered)
    );
    public static readonly EventId ErrorGroupingDeadLetterWriteFailed = new(
        1405,
        nameof(ErrorGroupingDeadLetterWriteFailed)
    );

    // 1406 ErrorGroupingRecordFailed, 3100 CatalogSnapshotReloadFailed and
    // 3200 FurnitureDefinitionReloadFailed: retired, never logged.
}

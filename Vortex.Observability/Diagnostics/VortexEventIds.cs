using Microsoft.Extensions.Logging;

namespace Vortex.Observability.Diagnostics;

/// <summary>
/// Centrally-allocated, stable <see cref="EventId"/>s. Ranges are reserved per concern so ids never
/// collide and can be referenced from log queries and dashboards:
/// <list type="bullet">
/// <item>1000-1099 observability core</item>
/// <item>1100-1199 networking / packet pipeline</item>
/// <item>1200-1299 audit pipeline</item>
/// <item>1300-1399 dashboard</item>
/// <item>2000-2099 authentication</item>
/// <item>3000-3099 economy</item>
/// <item>3100-3199 catalog</item>
/// <item>3200-3299 furniture</item>
/// </list>
/// New ids are appended here rather than declared inline at the call site.
/// </summary>
public static class VortexEventIds
{
    public static readonly EventId ObservabilityReady = new(1000, nameof(ObservabilityReady));
    public static readonly EventId TraceScopeFault = new(1001, nameof(TraceScopeFault));
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

    // The hotel-wide word filter, whose journal rides the audit channel.
    public static readonly EventId WordFilterHitDropped = new(1210, nameof(WordFilterHitDropped));
    public static readonly EventId WordFilterTimedOut = new(1211, nameof(WordFilterTimedOut));
    public static readonly EventId WordFilterLoaded = new(1212, nameof(WordFilterLoaded));
    public static readonly EventId WordFilterLoadFailed = new(1213, nameof(WordFilterLoadFailed));

    public static readonly EventId DashboardReady = new(1300, nameof(DashboardReady));
    public static readonly EventId DashboardDisabled = new(1301, nameof(DashboardDisabled));
    public static readonly EventId DashboardFault = new(1302, nameof(DashboardFault));

    public static readonly EventId HandlerPipelineError = new(1400, nameof(HandlerPipelineError));
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
    public static readonly EventId ErrorGroupingRecordFailed = new(
        1406,
        nameof(ErrorGroupingRecordFailed)
    );

    public static readonly EventId CatalogSnapshotReloadFailed = new(
        3100,
        nameof(CatalogSnapshotReloadFailed)
    );

    public static readonly EventId FurnitureDefinitionReloadFailed = new(
        3200,
        nameof(FurnitureDefinitionReloadFailed)
    );
}

namespace Vortex.Observability.Configuration;

/// <summary>
/// Strongly-typed options for the observability brick, bound from the <c>Vortex:Observability</c>
/// configuration section following the project's per-module options convention.
/// </summary>
public sealed class ObservabilityConfig
{
    public const string SECTION_NAME = "Vortex:Observability";

    /// <summary>Master switch for emitting <c>System.Diagnostics.Metrics</c> instruments.</summary>
    public bool MetricsEnabled { get; init; } = true;

    /// <summary>
    /// When true, an <c>Activity</c> span is started per operation (OpenTelemetry-ready). Spans are
    /// near-free when no listener is registered, so this can stay on by default.
    /// </summary>
    public bool TracingEnabled { get; init; } = true;

    /// <summary>Bounded capacity of the in-process audit queue before saturation drops occur.</summary>
    public int AuditChannelCapacity { get; init; } = 10_000;

    /// <summary>Maximum number of audit events persisted per database round-trip.</summary>
    public int AuditBatchSize { get; init; } = 200;

    /// <summary>Retry attempts when durable audit batch persistence fails.</summary>
    public int AuditWriteRetryAttempts { get; init; } = 2;

    /// <summary>Delay between retry attempts for failed durable audit writes.</summary>
    public int AuditWriteRetryDelayMs { get; init; } = 250;

    /// <summary>
    /// How long the audit trail is kept, in days. 0 keeps it for ever. This is what a dispute is
    /// settled with months later, so it outlives the privacy-bearing tables below by design.
    /// </summary>
    public int AuditRetentionDays { get; init; } = 180;

    /// <summary>
    /// How long item provenance is kept, in days. 0 (the default) keeps it for ever: an item's
    /// history is the item, and a rare furni outlives every retention window worth setting.
    /// </summary>
    public int ItemEventRetentionDays { get; init; }

    /// <summary>How long room visit history is kept, in days. 0 keeps it for ever.</summary>
    public int RoomVisitRetentionDays { get; init; } = 90;

    /// <summary>Hours between retention sweeps. Minimum one hour.</summary>
    public int RetentionSweepIntervalHours { get; init; } = 24;

    /// <summary>Rows deleted per statement, so a sweep never holds the table for long.</summary>
    public int RetentionBatchSize { get; init; } = 5_000;

    /// <summary>
    /// Ceiling on rows removed from one table per sweep. A hotel enabling retention for the first
    /// time works through its backlog over several cycles rather than in one long delete.
    /// </summary>
    public int RetentionMaxRowsPerCycle { get; init; } = 200_000;

    /// <summary>Path to dead-letter file for batches that cannot be written to DB.</summary>
    public string AuditDeadLetterPath { get; init; } = "logs/audit-dead-letter.jsonl";

    /// <summary>
    /// Dead-letter file of the error-grouping writer. Its own file: two writers appending to one
    /// path collide on the file lock and one of the batches is lost.
    /// </summary>
    public string ErrorGroupingDeadLetterPath { get; init; } = "logs/error-dead-letter.jsonl";

    /// <summary>Bounded capacity of the in-memory error-grouping queue.</summary>
    public int ErrorGroupingChannelCapacity { get; init; } = 10_000;

    /// <summary>Maximum number of grouped error rows persisted per writer batch.</summary>
    public int ErrorTrackingBatchSize { get; init; } = 150;

    /// <summary>Retry attempts when error-grouping writes fail.</summary>
    public int ErrorTrackingWriteRetryAttempts { get; init; } = 2;

    /// <summary>Delay between error-grouping write retries (milliseconds).</summary>
    public int ErrorTrackingWriteRetryDelayMs { get; init; } = 250;
}

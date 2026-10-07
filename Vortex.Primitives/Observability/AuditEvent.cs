namespace Vortex.Primitives.Observability;

/// <summary>
/// A single durable business-audit record. Deliberately separate from technical logs and from
/// metrics: it exists to answer "who did what, to whom, where, with what result, under which
/// correlation id". Mandatory fields are explicit; optional identity/target fields stay null when
/// not relevant to the action.
/// </summary>
public readonly record struct AuditEvent
{
    public required AuditCategory Category { get; init; }

    /// <summary>Stable action key, for example "auth.login.failed" or "session.connected".</summary>
    public required string Action { get; init; }

    public CorrelationId CorrelationId { get; init; }

    public AuditSeverity Severity { get; init; }

    public AuditResult Result { get; init; }

    public long? ActorPlayerId { get; init; }

    public long? TargetPlayerId { get; init; }

    public int? RoomId { get; init; }

    public long? ItemId { get; init; }

    /// <summary>Hashed source IP (never store the raw IP).</summary>
    public string? IpHash { get; init; }

    /// <summary>Optional structured (JSON) payload carrying action-specific detail.</summary>
    /// <summary>The number the event is about, promoted out of <see cref="Data" /> so a read can
    /// SUM it in the database instead of parsing every row's JSON.</summary>
    public long? Amount { get; init; }

    /// <summary>How many of whatever it was.</summary>
    public int? Quantity { get; init; }

    public string? Data { get; init; }
}

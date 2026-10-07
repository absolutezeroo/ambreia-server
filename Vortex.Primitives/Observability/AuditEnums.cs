namespace Vortex.Primitives.Observability;

/// <summary>Top-level grouping of audit records; maps to the durable audit category column.</summary>
public enum AuditCategory
{
    Auth,
    Staff,
    Moderation,
    Economy,
    Item,
    Room,
    Security,
    Social,
    System,

    /// <summary>
    /// Player progression: the player-life events that are neither social nor economic, and that an
    /// investigation reads as "what did this account actually do". Nothing records it since the cut
    /// of 2026-10-07 removed achievements, quests and badges; rows already written may carry it.
    /// Stored as a string, so appending here needs no migration.
    /// </summary>
    Progression,

    /// <summary>
    /// What a player told us, in their own words. Every other category records something the hotel
    /// observed; this one records something only the player can see — a room that did not draw, a
    /// window in the wrong place, an item that behaves oddly. None of it reaches the error
    /// grouping, because nothing threw.
    ///
    /// Deliberately its own category rather than a <see cref="System" /> action: an investigation
    /// filters by category first, and a bug report buried among scheduler events is a bug report
    /// nobody reads. Stored as a string, so appending here needs no migration.
    /// </summary>
    PlayerReport,
}

/// <summary>Severity of an audit record, used for incident triage and retention policy.</summary>
public enum AuditSeverity
{
    Info,
    Notice,
    Warning,
    Critical,
}

/// <summary>Outcome of the audited action.</summary>
public enum AuditResult
{
    Success,
    Failed,
    Denied,
}

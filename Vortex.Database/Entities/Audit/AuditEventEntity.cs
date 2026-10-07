using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Attributes;
using Vortex.Primitives.Observability;

namespace Vortex.Database.Entities.Audit;

/// <summary>
/// Durable business-audit record (the audit spine). Append-only by design: it answers "who did
/// what, to whom, where, with what result, under which correlation id". It is deliberately NOT a
/// foreign-key child of <c>players</c> — audit must survive player deletion and supports RGPD
/// anonymisation by replacing identity columns rather than cascading deletes.
/// Enum columns are stored as strings for human-readable forensic queries.
/// </summary>
[Table("audit_events")]
[Index(nameof(CorrelationId))]
[Index(nameof(IpHash))]
[Index(nameof(ActorPlayerId), nameof(OccurredAt))]
[Index(nameof(TargetPlayerId), nameof(OccurredAt))]
[Index(nameof(RoomId), nameof(OccurredAt))]
[Index(nameof(Category), nameof(OccurredAt))]
public class AuditEventEntity : VortexEntity
{
    [Column("occurred_at")]
    public required DateTime OccurredAt { get; set; }

    [Column("category")]
    [EnumStorage(typeof(string))]
    [MaxLength(32)]
    public required AuditCategory Category { get; set; }

    [Column("action")]
    [MaxLength(64)]
    public required string Action { get; set; }

    [Column("severity")]
    [EnumStorage(typeof(string))]
    [MaxLength(16)]
    public required AuditSeverity Severity { get; set; }

    [Column("result")]
    [EnumStorage(typeof(string))]
    [MaxLength(16)]
    public required AuditResult Result { get; set; }

    [Column("correlation_id")]
    [MaxLength(32)]
    public string? CorrelationId { get; set; }

    [Column("actor_player_id")]
    public long? ActorPlayerId { get; set; }

    [Column("target_player_id")]
    public long? TargetPlayerId { get; set; }

    [Column("room_id")]
    public int? RoomId { get; set; }

    [Column("item_id")]
    public long? ItemId { get; set; }

    [Column("ip_hash")]
    [MaxLength(64)]
    public string? IpHash { get; set; }

    /// <summary>
    /// The number this event is about, when it is about one. A column of its own rather than a field
    /// inside <see cref="Data" />, so a read that wants to sum it does not have to pull the whole
    /// window into memory and parse JSON. Null on the events that have no such number.
    /// </summary>
    [Column("amount")]
    public long? Amount { get; set; }

    /// <summary>How many of whatever it was. Same reason, same nullability.</summary>
    [Column("quantity")]
    public int? Quantity { get; set; }

    /// <summary>Optional structured (JSON) payload with action-specific detail.</summary>
    [Column("data", TypeName = "text")]
    public string? Data { get; set; }
}

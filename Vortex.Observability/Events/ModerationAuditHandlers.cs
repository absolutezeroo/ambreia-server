using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

/// <summary>Translates moderation domain events into durable audit records (category Moderation).</summary>
public sealed class PlayerKickedFromRoomAuditHandler(IAuditSink audit)
    : IEventHandler<PlayerKickedFromRoomEvent>
{
    public ValueTask HandleAsync(
        PlayerKickedFromRoomEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Moderation,
                Action = "moderation.kick",
                Severity = AuditSeverity.Notice,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                TargetPlayerId = e.TargetPlayerId,
                RoomId = e.RoomId,
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class PlayerMutedInRoomAuditHandler(IAuditSink audit)
    : IEventHandler<PlayerMutedInRoomEvent>
{
    public ValueTask HandleAsync(PlayerMutedInRoomEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Moderation,
                Action = "moderation.mute",
                Severity = AuditSeverity.Notice,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                TargetPlayerId = e.TargetPlayerId,
                RoomId = e.RoomId,
                Data = JsonSerializer.Serialize(new { durationSeconds = e.DurationSeconds }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class PlayerBannedInRoomAuditHandler(IAuditSink audit)
    : IEventHandler<PlayerBannedInRoomEvent>
{
    public ValueTask HandleAsync(PlayerBannedInRoomEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Moderation,
                Action = "moderation.ban",
                Severity = AuditSeverity.Warning,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                TargetPlayerId = e.TargetPlayerId,
                RoomId = e.RoomId,
                Data = JsonSerializer.Serialize(new { durationSeconds = e.DurationSeconds }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class PlayerAlertedAuditHandler(IAuditSink audit) : IEventHandler<PlayerAlertedEvent>
{
    public ValueTask HandleAsync(PlayerAlertedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Moderation,
                Action = "moderation.alert",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                TargetPlayerId = e.TargetPlayerId,
                RoomId = e.RoomId,
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class ModerationActionDeniedAuditHandler(IAuditSink audit)
    : IEventHandler<ModerationActionDeniedEvent>
{
    public ValueTask HandleAsync(
        ModerationActionDeniedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Moderation,
                Action = "moderation.denied",
                Severity = AuditSeverity.Warning,
                Result = AuditResult.Denied,
                ActorPlayerId = e.ActorPlayerId,
                TargetPlayerId = e.TargetPlayerId,
                RoomId = e.RoomId,
                Data = JsonSerializer.Serialize(new { action = e.Action }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class PlayerAccountBannedAuditHandler(IAuditSink audit)
    : IEventHandler<PlayerAccountBannedEvent>
{
    public ValueTask HandleAsync(PlayerAccountBannedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Moderation,
                Action = "moderation.account_ban",
                Severity = AuditSeverity.Warning,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                TargetPlayerId = e.TargetPlayerId,
                Data = JsonSerializer.Serialize(
                    new { bannedUntil = e.BannedUntil, reason = e.Reason }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class PlayerTradingLockedAuditHandler(IAuditSink audit)
    : IEventHandler<PlayerTradingLockedEvent>
{
    public ValueTask HandleAsync(PlayerTradingLockedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Moderation,
                Action = "moderation.trading_lock",
                Severity = AuditSeverity.Notice,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                TargetPlayerId = e.TargetPlayerId,
                Data = JsonSerializer.Serialize(new { lockedUntil = e.LockedUntil }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// The room tool's three checkboxes. Audited as Warning rather than Notice: unlike a kick aimed at
/// one person, this reaches every occupant at once and rewrites a room somebody owns.
/// </summary>
public sealed class RoomModeratedByStaffAuditHandler(IAuditSink audit)
    : IEventHandler<RoomModeratedByStaffEvent>
{
    public ValueTask HandleAsync(
        RoomModeratedByStaffEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Moderation,
                Action = "moderation.room.moderate",
                Severity = AuditSeverity.Warning,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                RoomId = e.RoomId,
                Data = JsonSerializer.Serialize(
                    new
                    {
                        doorUnlocked = e.DoorUnlocked,
                        nameReset = e.NameReset,
                        usersKicked = e.UsersKicked,
                    }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class CfhTicketsPickedAuditHandler(IAuditSink audit)
    : IEventHandler<CfhTicketsPickedEvent>
{
    public ValueTask HandleAsync(CfhTicketsPickedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Moderation,
                Action = "moderation.cfh.pick",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                Data = JsonSerializer.Serialize(new { issueIds = e.IssueIds }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class CfhTicketsReleasedAuditHandler(IAuditSink audit)
    : IEventHandler<CfhTicketsReleasedEvent>
{
    public ValueTask HandleAsync(CfhTicketsReleasedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Moderation,
                Action = "moderation.cfh.release",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                Data = JsonSerializer.Serialize(new { issueIds = e.IssueIds }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A player filed a report. Both sides are named so the line surfaces from either timeline: the
/// account asking for help, and the account it names. Only the moderator half of a ticket's life was
/// audited before this, which left the two questions an abuse pattern is made of unanswerable --
/// who keeps getting reported, and who keeps filing.
/// </summary>
public sealed class CfhTicketOpenedAuditHandler(IAuditSink audit)
    : IEventHandler<CfhTicketOpenedEvent>
{
    public ValueTask HandleAsync(CfhTicketOpenedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Moderation,
                Action = "moderation.cfh.opened",
                Severity = AuditSeverity.Notice,
                Result = AuditResult.Success,
                ActorPlayerId = e.ReporterPlayerId,
                TargetPlayerId = e.ReportedPlayerId,
                RoomId = e.RoomId,
                Data = JsonSerializer.Serialize(new { e.IssueId, e.TopicId }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class CfhTicketsClosedAuditHandler(IAuditSink audit)
    : IEventHandler<CfhTicketsClosedEvent>
{
    public ValueTask HandleAsync(CfhTicketsClosedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Moderation,
                Action = "moderation.cfh.close",
                Severity = AuditSeverity.Notice,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                Data = JsonSerializer.Serialize(
                    new
                    {
                        issueIds = e.IssueIds,
                        reason = e.Reason,
                        sanctioned = e.Sanctioned,
                    }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// The mod tool's mute. Recorded apart from the room mute because the two answer different
/// questions: this one is "was this person silenced across the hotel, and until when".
/// </summary>
public sealed class PlayerHotelMutedAuditHandler(IAuditSink audit)
    : IEventHandler<PlayerHotelMutedEvent>
{
    public ValueTask HandleAsync(PlayerHotelMutedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Moderation,
                Action = e.MutedUntil is null ? "moderation.unmute" : "moderation.mute.hotel",
                Severity = AuditSeverity.Notice,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                TargetPlayerId = e.TargetPlayerId,
                Data = JsonSerializer.Serialize(new { mutedUntil = e.MutedUntil }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class RoomAlertedByStaffAuditHandler(IAuditSink audit)
    : IEventHandler<RoomAlertedByStaffEvent>
{
    public ValueTask HandleAsync(RoomAlertedByStaffEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Moderation,
                Action = e.IsCaution ? "moderation.room.caution" : "moderation.room.message",
                Severity = AuditSeverity.Notice,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorPlayerId,
                RoomId = e.RoomId,
                Data = JsonSerializer.Serialize(new { message = e.Message }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

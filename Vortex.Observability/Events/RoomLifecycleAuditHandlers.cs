using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

/// <summary>
/// Room ownership and control as durable audit. Entering and leaving a room was already recorded;
/// what was missing is everything that changes the room itself -- who made it, who deleted it, who
/// was handed the keys. Rights in particular are how a room gets emptied by somebody who never
/// owned it, and there was no record of them changing hands at all.
/// </summary>
public sealed class RoomCreatedAuditHandler(IAuditSink audit) : IEventHandler<RoomCreatedEvent>
{
    public ValueTask HandleAsync(RoomCreatedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Room,
                Action = "room.created",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.OwnerId,
                RoomId = e.RoomId,
                Data = JsonSerializer.Serialize(new { e.Name }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class RoomDeletedAuditHandler(IAuditSink audit) : IEventHandler<RoomDeletedEvent>
{
    public ValueTask HandleAsync(RoomDeletedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Room,
                // Notice, not Info: a deleted room takes its furniture placement with it, and this
                // is often the last thing that happened before somebody asks where it all went.
                Severity = AuditSeverity.Notice,
                Action = "room.deleted",
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorId,
                RoomId = e.RoomId,
                Data = JsonSerializer.Serialize(new { e.Name }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class RoomSettingsUpdatedAuditHandler(IAuditSink audit)
    : IEventHandler<RoomSettingsUpdatedEvent>
{
    public ValueTask HandleAsync(RoomSettingsUpdatedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Room,
                // The section is in the action, not the payload: the audit page filters on action,
                // and "which dialog changed it" is exactly the question worth filtering on.
                Action = $"room.{e.Section}_updated",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorId,
                RoomId = e.RoomId,
                Data = JsonSerializer.Serialize(new { e.Name }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class RoomRightsChangedAuditHandler(IAuditSink audit)
    : IEventHandler<RoomRightsChangedEvent>
{
    public ValueTask HandleAsync(RoomRightsChangedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Room,
                Action = $"room.rights_{e.Change}",
                Severity = AuditSeverity.Notice,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorId,
                TargetPlayerId = e.TargetPlayerId?.Value,
                RoomId = e.RoomId,
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class RoomDoorbellAnsweredAuditHandler(IAuditSink audit)
    : IEventHandler<RoomDoorbellAnsweredEvent>
{
    public ValueTask HandleAsync(
        RoomDoorbellAnsweredEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Room,
                Action = e.Admitted ? "room.doorbell_admitted" : "room.doorbell_refused",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.ActorId,
                TargetPlayerId = e.TargetPlayerId,
                RoomId = e.RoomId,
            }
        );

        return ValueTask.CompletedTask;
    }
}

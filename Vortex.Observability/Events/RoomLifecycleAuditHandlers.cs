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

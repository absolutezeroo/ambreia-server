using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

public sealed class PlayerLeftRoomAuditHandler(IAuditSink audit)
    : IEventHandler<PlayerLeftRoomEvent>
{
    public ValueTask HandleAsync(PlayerLeftRoomEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Room,
                Action = "room.left",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                RoomId = e.RoomId,
                Data = JsonSerializer.Serialize(
                    new
                    {
                        e.RoomId,
                        e.LeftAtUtc,
                        durationSeconds = e.RoomDurationSeconds,
                    }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

public sealed class PlayerDisconnectedAuditHandler(IAuditSink audit)
    : IEventHandler<PlayerDisconnectedEvent>
{
    public ValueTask HandleAsync(PlayerDisconnectedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Auth,
                Action = "session.disconnected",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(
                    new
                    {
                        connectedAtUtc = e.ConnectedAtUtc,
                        disconnectedAtUtc = e.DisconnectedAtUtc,
                        durationSeconds = e.SessionDurationSeconds,
                    }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}

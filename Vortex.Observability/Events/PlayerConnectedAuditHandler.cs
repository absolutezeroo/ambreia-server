using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

/// <summary>Transforms player session/room lifecycle events into durable audit records.</summary>
public sealed class PlayerConnectedAuditHandler(IAuditSink audit)
    : IEventHandler<PlayerConnectedEvent>
{
    public ValueTask HandleAsync(PlayerConnectedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Auth,
                Action = "session.connected",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(new { connectedAtUtc = e.ConnectedAtUtc }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

/// <summary>Translates authentication domain events into durable audit records.</summary>
public sealed class PlayerLoggedInAuditHandler(IAuditSink audit)
    : IEventHandler<PlayerLoggedInEvent>
{
    public ValueTask HandleAsync(PlayerLoggedInEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Auth,
                Action = "auth.login.success",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                IpHash = e.IpHash,
            }
        );

        return ValueTask.CompletedTask;
    }
}

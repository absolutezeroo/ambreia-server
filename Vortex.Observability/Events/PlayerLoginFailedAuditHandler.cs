using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

public sealed class PlayerLoginFailedAuditHandler(IAuditSink audit)
    : IEventHandler<PlayerLoginFailedEvent>
{
    public ValueTask HandleAsync(PlayerLoginFailedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Auth,
                Action = "auth.login.failed",
                Severity = AuditSeverity.Notice,
                Result = AuditResult.Failed,
                IpHash = e.IpHash,
            }
        );

        return ValueTask.CompletedTask;
    }
}

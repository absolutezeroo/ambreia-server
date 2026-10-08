using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

public sealed class PlayerNameChangedAuditHandler(IAuditSink audit)
    : IEventHandler<PlayerNameChangedEvent>
{
    public ValueTask HandleAsync(PlayerNameChangedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Security,
                Action = "security.name.changed",
                // The single most confusing thing that can happen to an investigation: every older
                // record names somebody who, from the directory's point of view, no longer exists.
                Severity = AuditSeverity.Notice,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(new { from = e.OldName, to = e.NewName }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

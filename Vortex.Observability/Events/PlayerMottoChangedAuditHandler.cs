using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

/// <summary>The other half of the public identity, kept for the same reason as the figure.</summary>
public sealed class PlayerMottoChangedAuditHandler(IAuditSink audit)
    : IEventHandler<PlayerMottoChangedEvent>
{
    public ValueTask HandleAsync(PlayerMottoChangedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Social,
                Action = "profile.motto_changed",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(new { motto = e.Motto }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

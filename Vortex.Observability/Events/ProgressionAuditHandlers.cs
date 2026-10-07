using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

/// <summary>An avatar effect was activated and started burning its duration.</summary>
public sealed class AvatarEffectActivatedAuditHandler(IAuditSink audit)
    : IEventHandler<AvatarEffectActivatedEvent>
{
    public ValueTask HandleAsync(
        AvatarEffectActivatedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Progression,
                Action = "effect.activated",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(new { e.EffectId, duration = e.DurationSeconds }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

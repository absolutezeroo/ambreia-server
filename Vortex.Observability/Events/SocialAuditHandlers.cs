using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

/// <summary>
/// The public half of an account's identity. Cheap to change, and the first thing changed when
/// someone is impersonating a player or covering their tracks, so the old value is kept in the
/// record rather than only the new one.
/// </summary>
public sealed class PlayerFigureChangedAuditHandler(IAuditSink audit)
    : IEventHandler<PlayerFigureChangedEvent>
{
    public ValueTask HandleAsync(PlayerFigureChangedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Social,
                Action = "profile.figure_changed",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(new { figure = e.Figure }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

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

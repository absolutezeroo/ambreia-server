using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

/// <summary>
/// Records every prize a pool paid out, whatever handed it over. One action for all reward furniture
/// on purpose: a disputed prize is checked against the pool it came from, and an operator comparing
/// what a pool really paid out against what its weights promise needs those rows in one place rather
/// than one action per furniture type.
/// </summary>
public sealed class LootAwardedAuditHandler(IAuditSink audit) : IEventHandler<LootAwardedEvent>
{
    public ValueTask HandleAsync(LootAwardedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Item,
                Action = "loot.awarded",
                Severity = AuditSeverity.Notice,
                Result = AuditResult.Success,
                TargetPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(
                    new
                    {
                        pool = e.PoolCode,
                        entryId = e.EntryId,
                        variant = e.Variant,
                        contentType = e.ContentType,
                        classId = e.ClassId,
                        source = e.Source,
                    }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}

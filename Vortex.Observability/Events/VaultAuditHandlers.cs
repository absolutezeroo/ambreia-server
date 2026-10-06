using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

/// <summary>Vault income cashed out, on the account's audit timeline.</summary>
public sealed class VaultIncomeClaimedAuditHandler(IAuditSink audit)
    : IEventHandler<VaultIncomeClaimedEvent>
{
    public ValueTask HandleAsync(VaultIncomeClaimedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Economy,
                Action = "economy.vault.income_claimed",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Quantity = e.Rewards,
                Data = JsonSerializer.Serialize(new { category = e.Category }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

/// <summary>Records every wallet movement in the durable economy ledger (intent derived from sign).</summary>
public sealed class CurrencyChangedLedgerHandler(IEconomyLedger ledger)
    : IEventHandler<CurrencyChangedEvent>
{
    public ValueTask HandleAsync(CurrencyChangedEvent e, EventContext ctx, CancellationToken ct)
    {
        if (e.Delta == 0)
        {
            return ValueTask.CompletedTask;
        }

        ledger.Record(
            new EconomyLedgerEvent
            {
                PlayerId = e.PlayerId,
                Currency = e.Currency,
                ActivityPointType = e.ActivityPointType,
                Delta = e.Delta,
                BalanceAfter = e.BalanceAfter,
                Reason = e.Delta < 0 ? EconomyReason.Debit : EconomyReason.Grant,
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class CatalogPurchasedAuditHandler(IAuditSink audit)
    : IEventHandler<CatalogPurchasedEvent>
{
    public ValueTask HandleAsync(CatalogPurchasedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Economy,
                Action = "economy.catalog_purchase",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                // Also in Data, and deliberately: rows written before these columns existed only
                // have the JSON, so the read falls back to it and the two must agree.
                // The offer goes in the id column the row already has, so the whole read groups in
                // SQL rather than opening every payload to find out what was bought.
                ItemId = e.OfferId,
                Amount = e.CreditCost,
                Quantity = e.Quantity,
                Data = JsonSerializer.Serialize(
                    new
                    {
                        catalogType = e.CatalogType,
                        offerId = e.OfferId,
                        quantity = e.Quantity,
                        creditCost = e.CreditCost,
                    }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}

public sealed class CatalogGiftPurchasedAuditHandler(IAuditSink audit)
    : IEventHandler<CatalogGiftPurchasedEvent>
{
    public ValueTask HandleAsync(
        CatalogGiftPurchasedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Economy,
                Action = "economy.catalog_gift",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.BuyerPlayerId,
                TargetPlayerId = e.ReceiverPlayerId,
                Data = JsonSerializer.Serialize(
                    new { offerId = e.OfferId, creditCost = e.CreditCost }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}

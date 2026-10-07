using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

/// <summary>Records Membership / VIP purchases and renewals on the economy audit trail.</summary>
public sealed class MembershipPurchasedAuditHandler(IAuditSink audit)
    : IEventHandler<MembershipPurchasedEvent>
{
    public ValueTask HandleAsync(MembershipPurchasedEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Economy,
                Action = e.IsRenewal ? "economy.hc.renew" : "economy.hc.purchase",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(
                    new
                    {
                        months = e.Months,
                        isVip = e.IsVip,
                        isRenewal = e.IsRenewal,
                        creditCost = e.CreditCost,
                        totalMonths = e.TotalMonths,
                    }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>Records club gift tokens granted to a player on their gift cycle.</summary>
public sealed class MembershipGiftTokenGrantedAuditHandler(IAuditSink audit)
    : IEventHandler<MembershipGiftTokenGrantedEvent>
{
    public ValueTask HandleAsync(
        MembershipGiftTokenGrantedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Economy,
                Action = "economy.hc.gift.token_granted",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(
                    new { tokens = e.Tokens, totalAvailable = e.TotalAvailable }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>Records a club gift claim.</summary>
public sealed class MembershipGiftClaimedAuditHandler(IAuditSink audit)
    : IEventHandler<MembershipGiftClaimedEvent>
{
    public ValueTask HandleAsync(
        MembershipGiftClaimedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Economy,
                Action = "economy.hc.gift.claimed",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(new { productCode = e.ProductCode }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>Records a Membership kickback (payday) reward payout.</summary>
public sealed class MembershipPaydayAuditHandler(IAuditSink audit)
    : IEventHandler<MembershipPaydayEvent>
{
    public ValueTask HandleAsync(MembershipPaydayEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Economy,
                Action = "economy.hc.payday",
                Severity = AuditSeverity.Info,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(new { credits = e.Credits }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

/// <summary>Records a Membership membership expiry.</summary>
public sealed class MembershipExpiredAuditHandler(IAuditSink audit)
    : IEventHandler<MembershipExpiredEvent>
{
    public ValueTask HandleAsync(MembershipExpiredEvent e, EventContext ctx, CancellationToken ct)
    {
        audit.Emit(
            new AuditEvent
            {
                Category = AuditCategory.Economy,
                Action = "economy.hc.expired",
                Severity = AuditSeverity.Notice,
                Result = AuditResult.Success,
                ActorPlayerId = e.PlayerId,
                Data = JsonSerializer.Serialize(new { wasVip = e.WasVip }),
            }
        );

        return ValueTask.CompletedTask;
    }
}

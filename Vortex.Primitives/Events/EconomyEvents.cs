namespace Vortex.Primitives.Events;

/// <summary>
/// A player's wallet balance changed. <paramref name="Delta"/> is signed (negative = debit). This is
/// the single source of truth for currency movement; consumers derive intent from the sign.
/// </summary>
public sealed record CurrencyChangedEvent(
    int PlayerId,
    string Currency,
    int? ActivityPointType,
    long Delta,
    long BalanceAfter
) : IEvent;

/// <summary>
/// Raised before anything durable happens in a catalog purchase — before the wallet is touched and
/// before the commerce journal opens — and published cancellably: a behaviour that sets
/// <c>Cancel</c> refuses the purchase the same way an unknown offer does.
/// </summary>
public sealed record CatalogPurchasingEvent(
    int PlayerId,
    string CatalogType,
    int OfferId,
    int Quantity,
    int CreditCost
) : IEvent;

/// <summary>A player completed a catalog purchase.</summary>
public sealed record CatalogPurchasedEvent(
    int PlayerId,
    string CatalogType,
    int OfferId,
    int Quantity,
    int CreditCost,
    // Which purchase this is. The relay delivers at-least-once, so a consumer that changes player
    // state — quest progress, a daily task — has to be able to tell a redelivery from a second
    // purchase, and nothing else on this event can. Empty for the events raised outside a commerce
    // operation, which are the ones nobody replays.
    string OperationId = ""
) : IEvent;

/// <summary>
/// A player bought a catalog offer for someone else. Raised alongside <see cref="CatalogPurchasedEvent"/>
/// -- that one records the spend, this one records who received the goods, which is the part a gift
/// adds and the part an operator needs when tracing where furniture came from.
/// </summary>
public sealed record CatalogGiftPurchasedEvent(
    int BuyerPlayerId,
    int ReceiverPlayerId,
    int OfferId,
    int CreditCost
) : IEvent;

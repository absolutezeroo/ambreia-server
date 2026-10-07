using System;
using System.Collections.Generic;

namespace Vortex.Primitives.Shop;

/// <summary>
/// What a shop product hands over once it is paid for. The <em>only</em> thing that decides which
/// grain is called, so a product row can never be made to pay out something the code does not know
/// how to grant.
/// </summary>
/// <remarks>
/// Membership and VIP are two members rather than one plus a boolean column because the level is the whole
/// difference between them (<c>ApplyMembershipMonthsAsync</c> writes 1 or 2), and a boolean that only one
/// kind reads is a column every other row has to mean nothing by.
/// </remarks>
public enum ShopProductKind
{
    Credits = 0,
    Duckets = 1,
    Diamonds = 2,

    /// <summary>Membership. <c>Amount</c> is a number of months.</summary>
    Membership = 3,

    /// <summary>Membership at VIP level. <c>Amount</c> is a number of months.</summary>
    MembershipVip = 4,
}

/// <summary>
/// Where an order is. The state is the server's alone: nothing the browser sends moves it, and in
/// particular nothing the browser is redirected BACK to. The only transition that grants anything is
/// <see cref="Pending"/> → <see cref="Paid"/>, and only a signed provider notification performs it.
/// </summary>
public enum ShopOrderState
{
    /// <summary>Opened, nothing paid. The player may still walk away and nothing has happened.</summary>
    Pending = 0,

    /// <summary>The provider says the money was captured. Past the pivot: this is now owed.</summary>
    Paid = 1,

    /// <summary>Paid and handed over.</summary>
    Fulfilled = 2,

    /// <summary>The provider says it will not be paid — expired, abandoned, refused.</summary>
    Cancelled = 3,

    /// <summary>
    /// Paid, and the hotel could not hand it over. Never resolved by guessing: the commerce journal
    /// holds the operation past its pivot and the existing "stuck past its pivot" alert is what
    /// surfaces it.
    /// </summary>
    NeedsIntervention = 4,
}

/// <summary>
/// One thing on sale. The price travels with it for DISPLAY only — the browser never sends a price
/// back, and <see cref="IShopService.StartOrderAsync"/> takes a product code and reads the amount
/// from the database itself.
/// </summary>
/// <param name="PriceMinor">The price in the currency's minor unit — 450 is 4,50 €. Never a float: money.</param>
/// <param name="Currency">ISO 4217, e.g. <c>EUR</c>.</param>
/// <param name="Icon">Which of the shop's product sprites the tile draws.</param>
public sealed record ShopProduct(
    string Code,
    ShopProductKind Kind,
    int Amount,
    int PriceMinor,
    string Currency,
    string Section,
    int Icon,
    bool Featured
);

/// <summary>One group of products, as the store page lays them out.</summary>
public sealed record ShopSection(string Code, IReadOnlyList<ShopProduct> Products);

/// <summary>Everything on sale, grouped. Anonymous: the store page renders signed out.</summary>
public sealed record ShopCatalog(IReadOnlyList<ShopSection> Sections);

/// <summary>
/// One order as its owner sees it. Every money field is the snapshot taken when the order was
/// opened, not a join to the product: a price change must not rewrite what someone already agreed
/// to pay, and the webhook checks the amount it is told against THIS.
/// </summary>
public sealed record ShopOrder(
    string Id,
    string ProductCode,
    ShopProductKind Kind,
    int Amount,
    int PriceMinor,
    string Currency,
    ShopOrderState State,
    string Provider,
    DateTime CreatedAt
);

/// <summary>
/// A freshly opened order and where to send the browser to pay for it.
/// </summary>
/// <remarks>
/// <see cref="RedirectUrl"/> is null when the provider hosts no payment page — the manual provider
/// is exactly that — and the site then shows the order as pending rather than navigating nowhere.
/// Whatever the browser does at that URL, it is never what grants the goods: see
/// <see cref="IShopService.HandleWebhookAsync"/>.
/// </remarks>
public sealed record ShopOrderStart(ShopOrder Order, string? RedirectUrl);

/// <summary>Why an order could not be opened. The site turns the code into French.</summary>
public enum ShopOrderRefusal
{
    None = 0,

    /// <summary>No such product, or it is not on sale.</summary>
    UnknownProduct = 1,

    /// <summary>The account already has more orders open than it is allowed to.</summary>
    TooManyOpen = 2,

    /// <summary>The shop is switched off, or its provider is not configured.</summary>
    Unavailable = 3,
}

/// <summary>The answer to <see cref="IShopService.StartOrderAsync"/>: one or the other, never both.</summary>
public sealed record ShopOrderResult(ShopOrderStart? Start, ShopOrderRefusal Refusal)
{
    public static ShopOrderResult Opened(ShopOrderStart start) => new(start, ShopOrderRefusal.None);

    public static ShopOrderResult Refused(ShopOrderRefusal refusal) => new(null, refusal);
}

/// <summary>
/// One inbound provider notification, exactly as it arrived. The body is the RAW text, not a parsed
/// object, because the signature is over the bytes the provider sent: reserialising a parsed body
/// changes whitespace and key order, and the signature stops matching for reasons that look like an
/// attack.
/// </summary>
public sealed record ShopWebhookRequest(
    string Provider,
    string Body,
    IReadOnlyDictionary<string, string> Headers
);

/// <summary>What the hotel did with a provider notification.</summary>
public enum ShopWebhookOutcome
{
    /// <summary>Read, verified, and acted on — including "acted on before", which answers the same.</summary>
    Accepted = 0,

    /// <summary>No provider is registered under that key, or it is not configured.</summary>
    UnknownProvider = 1,

    /// <summary>
    /// The signature is missing, malformed, stale or wrong. Answered identically in all four cases:
    /// telling a caller WHICH of them it was is how a forger narrows in.
    /// </summary>
    BadSignature = 2,

    /// <summary>Well signed, but names an order this hotel has never opened.</summary>
    UnknownOrder = 3,

    /// <summary>
    /// Well signed and about a real order, but the amount or currency is not the one the order was
    /// opened at. Never granted: the order is parked for an operator instead.
    /// </summary>
    AmountMismatch = 4,
}

/// <summary>
/// What a provider's notification says, once its own format has been read and its signature checked.
/// </summary>
/// <param name="Reference">
/// The provider's own id for the payment, stored on the order and unique per provider — the second
/// line of defence against a replayed notification, after the signature's timestamp and before the
/// order's own state check.
/// </param>
public sealed record ShopPaymentNotification(
    string OrderId,
    string Reference,
    int PaidMinor,
    string Currency,
    bool Paid
);

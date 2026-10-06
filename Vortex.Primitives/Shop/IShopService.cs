using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Shop;

/// <summary>
/// The hotel's paid shop: what is on sale, the orders an account has opened, and the one path that
/// turns a captured payment into credits, duckets, diamonds or club months.
/// </summary>
/// <remarks>
/// <para>
/// Five rules hold the whole thing up, and every one of them is a thing that has gone wrong in
/// somebody else's hotel:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>The browser never names a price.</b> <see cref="StartOrderAsync"/> takes a product code. The
/// amount, the price and the currency are read from the product row and SNAPSHOT onto the order, so
/// a caller cannot buy 1000 credits for one cent by editing the request, and a later price change
/// cannot rewrite an order already agreed to.
/// </description></item>
/// <item><description>
/// <b>The hotel never sees card data.</b> The provider hosts the payment page; see
/// <see cref="IShopPaymentProvider"/>.
/// </description></item>
/// <item><description>
/// <b>Only the signed webhook grants.</b> Not the return URL the browser lands on after paying —
/// that is a navigation, and a navigation is something anyone can perform. <see cref="GetOrderAsync"/>
/// is what the return page reads, and it reports state rather than changing it.
/// </description></item>
/// <item><description>
/// <b>The same notification twice grants once.</b> The operation id is derived from the order
/// (<c>CommerceOperationId.Deterministic</c>), the state transition is conditional on the order still
/// being Pending, and the wallet credit and its receipt commit together.
/// </description></item>
/// <item><description>
/// <b>The payment is the pivot, and it happens before the hotel hears about it.</b> A paid order the
/// hotel could not hand over is OWED, never refunded and never silently dropped: it sits past its
/// pivot in the commerce journal, where the existing alert finds it.
/// </description></item>
/// </list>
/// </remarks>
public interface IShopService
{
    /// <summary>Everything on sale, grouped into the store page's sections. Anonymous.</summary>
    Task<ShopCatalog> GetCatalogAsync(CancellationToken ct);

    /// <summary>
    /// Opens an order for one product and asks the provider to start the payment. Nothing is charged
    /// and nothing is granted here.
    /// </summary>
    Task<ShopOrderResult> StartOrderAsync(int playerId, string productCode, CancellationToken ct);

    /// <summary>
    /// One of this player's orders, or null. Scoped to the owner on purpose: an order id is not a
    /// capability, and someone else's order is a 404, not a 403.
    /// </summary>
    Task<ShopOrder?> GetOrderAsync(int playerId, string orderId, CancellationToken ct);

    /// <summary>This player's orders, newest first. What the purchases page lists.</summary>
    Task<IReadOnlyList<ShopOrder>> GetOrdersAsync(int playerId, CancellationToken ct);

    /// <summary>
    /// Verifies, reads and acts on one provider notification. The only method in the hotel that can
    /// move an order to <see cref="ShopOrderState.Paid"/>.
    /// </summary>
    Task<ShopWebhookOutcome> HandleWebhookAsync(ShopWebhookRequest request, CancellationToken ct);
}

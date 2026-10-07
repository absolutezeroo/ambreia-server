using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Shop;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Shop;
using Vortex.WebApi.Shop.Payments;

namespace Vortex.WebApi.Shop;

/// <summary>
/// The shop, end to end. See <see cref="IShopService"/> for the five rules this is an implementation
/// of; what follows is only how each one is actually enforced.
/// </summary>
internal sealed class ShopService(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IGrainFactory grainFactory,
    ICommerceJournal journal,
    IEnumerable<IShopPaymentProvider> providers,
    IOptions<ShopConfig> options,
    ILogger<ShopService> logger
) : IShopService
{
    private readonly ShopConfig _config = options.Value;

    private readonly Dictionary<string, IShopPaymentProvider> _providers = providers.ToDictionary(
        provider => provider.Key,
        StringComparer.OrdinalIgnoreCase
    );

    public async Task<ShopCatalog> GetCatalogAsync(CancellationToken ct)
    {
        await using VortexDbContext db = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        List<ShopProductEntity> rows = await db
            .ShopProducts.AsNoTracking()
            .Where(p => p.IsActive && p.DeletedAt == null)
            .OrderBy(p => p.Section)
            .ThenBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Grouped in memory rather than by a second query: the whole table is a couple of dozen rows
        // and the page wants all of them.
        List<ShopSection> sections = rows.GroupBy(p => p.Section, StringComparer.Ordinal)
            .Select(group => new ShopSection(group.Key, group.Select(ToProduct).ToList()))
            .ToList();

        return new ShopCatalog(sections);
    }

    public async Task<ShopOrderResult> StartOrderAsync(
        int playerId,
        string productCode,
        CancellationToken ct
    )
    {
        if (!_config.Enabled || !TryResolveConfiguredProvider(out IShopPaymentProvider provider))
        {
            return ShopOrderResult.Refused(ShopOrderRefusal.Unavailable);
        }

        await using VortexDbContext db = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        // The product is read HERE and the amount, price and currency are taken from it. The caller
        // sent a code and nothing else; there is no request field this could be overridden by,
        // because there is no such field.
        ShopProductEntity? product = await db
            .ShopProducts.AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.Code == productCode && p.IsActive && p.DeletedAt == null,
                ct
            )
            .ConfigureAwait(false);

        if (product is null || product.Amount <= 0 || product.PriceMinor <= 0)
        {
            // A zero-priced or zero-amount row is content that would give something away or take
            // money for nothing. Refusing beats picking one of the two.
            return ShopOrderResult.Refused(ShopOrderRefusal.UnknownProduct);
        }

        int open = await db
            .ShopOrders.CountAsync(
                o => o.PlayerEntityId == playerId && o.State == ShopOrderState.Pending,
                ct
            )
            .ConfigureAwait(false);

        if (open >= _config.MaximumOpenOrders)
        {
            return ShopOrderResult.Refused(ShopOrderRefusal.TooManyOpen);
        }

        ShopOrderEntity entity = new()
        {
            PublicId = Guid.CreateVersion7(),
            PlayerEntityId = playerId,
            ProductCode = product.Code,
            Kind = product.Kind,
            Amount = product.Amount,
            PriceMinor = product.PriceMinor,
            Currency = product.Currency,
            State = ShopOrderState.Pending,
            Provider = provider.Key,
        };

        db.ShopOrders.Add(entity);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        ShopOrder order = ToOrder(entity);
        string? redirect = null;

        try
        {
            redirect = await provider.StartAsync(order, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The order stays Pending with no redirect, which is true: nothing was charged. The
            // player can start another one, and this one expires on its own.
            logger.LogError(
                ex,
                "Provider {Provider} could not open payment for order {OrderId}.",
                provider.Key,
                entity.PublicId
            );
        }

        logger.LogInformation(
            "Player {PlayerId} opened order {OrderId} for {ProductCode} ({PriceMinor} {Currency}) via {Provider}.",
            playerId,
            entity.PublicId,
            product.Code,
            product.PriceMinor,
            product.Currency,
            provider.Key
        );

        return ShopOrderResult.Opened(new ShopOrderStart(order, redirect));
    }

    public async Task<ShopOrder?> GetOrderAsync(int playerId, string orderId, CancellationToken ct)
    {
        if (!Guid.TryParse(orderId, out Guid publicId))
        {
            return null;
        }

        await using VortexDbContext db = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        // Scoped to the owner inside the query. An order id is an identifier, never a capability:
        // someone else's order has to be indistinguishable from one that does not exist.
        ShopOrderEntity? entity = await db
            .ShopOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.PublicId == publicId && o.PlayerEntityId == playerId, ct)
            .ConfigureAwait(false);

        return entity is null ? null : ToOrder(entity);
    }

    public async Task<IReadOnlyList<ShopOrder>> GetOrdersAsync(int playerId, CancellationToken ct)
    {
        await using VortexDbContext db = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        List<ShopOrderEntity> rows = await db
            .ShopOrders.AsNoTracking()
            .Where(o => o.PlayerEntityId == playerId)
            .OrderByDescending(o => o.Id)
            .Take(100)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.ConvertAll(ToOrder);
    }

    public async Task<ShopWebhookOutcome> HandleWebhookAsync(
        ShopWebhookRequest request,
        CancellationToken ct
    )
    {
        if (
            !_config.Enabled
            || !_providers.TryGetValue(request.Provider, out IShopPaymentProvider? provider)
            || !TryGetSecret(provider.Key, out string secret)
        )
        {
            // An unconfigured provider answers exactly like a nonexistent one. Which providers a
            // hotel has contracts with is not something a probe should be able to enumerate.
            return ShopWebhookOutcome.UnknownProvider;
        }

        // Before anything reads the body. A body that failed this check is not evidence of anything
        // and must not reach a parser, let alone a database.
        if (
            !WebhookSignature.Verify(
                request.Headers,
                request.Body,
                secret,
                TimeSpan.FromSeconds(_config.WebhookToleranceSeconds),
                DateTimeOffset.UtcNow
            )
        )
        {
            logger.LogWarning(
                "Rejected a {Provider} webhook: the signature did not verify.",
                provider.Key
            );

            return ShopWebhookOutcome.BadSignature;
        }

        if (
            !provider.TryReadNotification(request, out ShopPaymentNotification notification)
            || !Guid.TryParse(notification.OrderId, out Guid publicId)
        )
        {
            return ShopWebhookOutcome.UnknownOrder;
        }

        await using VortexDbContext db = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        ShopOrderEntity? order = await db
            .ShopOrders.FirstOrDefaultAsync(
                o => o.PublicId == publicId && o.Provider == provider.Key,
                ct
            )
            .ConfigureAwait(false);

        if (order is null)
        {
            return ShopWebhookOutcome.UnknownOrder;
        }

        if (!notification.Paid)
        {
            await CancelAsync(db, order, ct).ConfigureAwait(false);

            return ShopWebhookOutcome.Accepted;
        }

        // The amount the provider says it captured against what this order has always meant. A
        // mismatch is never granted and never guessed at: it is either a mis-integrated provider or
        // someone paying one cent for a thousand credits, and both want a human.
        if (
            notification.PaidMinor != order.PriceMinor
            || !string.Equals(
                notification.Currency,
                order.Currency,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            logger.LogError(
                "Order {OrderId} was opened at {Expected} {ExpectedCurrency} but {Provider} reports "
                    + "{Paid} {PaidCurrency}. Nothing was granted.",
                order.PublicId,
                order.PriceMinor,
                order.Currency,
                provider.Key,
                notification.PaidMinor,
                notification.Currency
            );

            order.State = ShopOrderState.NeedsIntervention;

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return ShopWebhookOutcome.AmountMismatch;
        }

        await CaptureAsync(db, order, notification, ct).ConfigureAwait(false);

        // Outside the "did this call capture it" branch on purpose. A notification delivered twice
        // must produce the same end state, and the second delivery's job is to make sure the grant
        // that the first one may have died halfway through has actually happened.
        await FulfilAsync(db, order, ct).ConfigureAwait(false);

        return ShopWebhookOutcome.Accepted;
    }

    /// <summary>
    /// Moves the order to <see cref="ShopOrderState.Paid"/> if it is still pending, conditionally, so
    /// two notifications racing cannot both claim it.
    /// </summary>
    private async Task CaptureAsync(
        VortexDbContext db,
        ShopOrderEntity order,
        ShopPaymentNotification notification,
        CancellationToken ct
    )
    {
        if (order.State != ShopOrderState.Pending)
        {
            return;
        }

        order.State = ShopOrderState.Paid;
        order.ProviderReference = notification.Reference;
        order.PaidAt = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // Almost certainly the unique (provider, provider_reference) index: the same payment
            // arriving on a second order, or the same notification racing itself. Nothing is granted
            // off a write that did not land — the fulfilment below reads the journal, not this row.
            logger.LogWarning(
                ex,
                "Order {OrderId} could not be marked paid; reference {Reference} is already recorded.",
                order.PublicId,
                notification.Reference
            );
        }
    }

    private async Task CancelAsync(VortexDbContext db, ShopOrderEntity order, CancellationToken ct)
    {
        // Only from Pending. A provider that says "not paid" about an order already captured is
        // reporting a refund or a chargeback, and taking the goods back is a decision with a policy
        // behind it — not something a webhook handler invents.
        if (order.State != ShopOrderState.Pending)
        {
            logger.LogWarning(
                "A not-paid notification arrived for order {OrderId}, which is {State}. Ignored.",
                order.PublicId,
                order.State
            );

            return;
        }

        order.State = ShopOrderState.Cancelled;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Hands over what a paid order bought. Opened past its pivot, because the pivot — the capture —
    /// happened at the provider before the hotel was told.
    /// </summary>
    /// <remarks>
    /// Safe to call any number of times for one order: the operation id is derived from the order, so
    /// every attempt is the same operation asking again, and the grant itself is guarded per step.
    /// </remarks>
    private async Task FulfilAsync(VortexDbContext db, ShopOrderEntity order, CancellationToken ct)
    {
        CommerceOperationId operation = CommerceOperationId.Deterministic(
            CommerceOperationKind.ShopPurchase,
            order.Id
        );

        await journal
            .OpenIfNewAsync(
                operation,
                CommerceOperationKind.ShopPurchase,
                order.PlayerEntityId,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"order={order.PublicId} product={order.ProductCode} {order.Kind}x{order.Amount} "
                        + $"paid={order.PriceMinor} {order.Currency} via {order.Provider}"
                ),
                ct
            )
            .ConfigureAwait(false);

        await journal
            .TransitionAsync(
                operation,
                CommerceOperationState.Pivoted,
                CommerceStepKeys.SHOP_GRANT,
                null,
                ct
            )
            .ConfigureAwait(false);

        bool granted;

        try
        {
            granted = await GrantAsync(order, operation, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Order {OrderId} is paid and its grant threw. It is owed.",
                order.PublicId
            );

            granted = false;
        }

        if (!granted)
        {
            // Past the pivot, so there is no refund to reach for: the money is at the provider and
            // the player is owed the goods. Left as Pivoted in the journal, which is exactly what the
            // "stuck past its pivot" alert reads, and marked on the order so the site can say so.
            await journal
                .TransitionAsync(
                    operation,
                    CommerceOperationState.Pivoted,
                    CommerceStepKeys.SHOP_GRANT,
                    "grant did not land",
                    ct
                )
                .ConfigureAwait(false);

            order.State = ShopOrderState.NeedsIntervention;

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return;
        }

        await journal
            .TransitionAsync(operation, CommerceOperationState.Completed, null, null, ct)
            .ConfigureAwait(false);

        order.State = ShopOrderState.Fulfilled;
        order.FulfilledAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        logger.LogInformation(
            "Order {OrderId} fulfilled: {Kind} x{Amount} to player {PlayerId}.",
            order.PublicId,
            order.Kind,
            order.Amount,
            order.PlayerEntityId
        );
    }

    private async Task<bool> GrantAsync(
        ShopOrderEntity order,
        CommerceOperationId operation,
        CancellationToken ct
    )
    {
        PlayerId player = new(order.PlayerEntityId);

        if (order.Kind is ShopProductKind.Membership or ShopProductKind.MembershipVip)
        {
            // The club write takes the operation id now, so the months and the receipt for them
            // commit together and this is safe to repeat — the same property the wallet branch below
            // has always had.
            //
            // It used to claim the step first and then act, because the grant added months to
            // whatever was there and could not be repeated. The ceiling that comment named was a
            // crash between the two writes: the months stayed owed with the step already marked
            // done, so a replayed webhook was answered "already granted" and FulfilAsync closed the
            // order as delivered. A paid order that was never delivered became indistinguishable
            // from one that was.
            MembershipPurchaseResult result = await grainFactory
                .GetPlayerGrain(player)
                .GrantMembershipMonthsAsync(
                    order.Amount,
                    order.Kind == ShopProductKind.MembershipVip,
                    operation,
                    ct
                )
                .ConfigureAwait(false);

            return result == MembershipPurchaseResult.Success;
        }

        CurrencyKind currency = new() { CurrencyType = ToCurrencyType(order.Kind) };

        // CreditOnceAsync, not GrantCurrencyAsync: the credit and the receipt that says it happened
        // commit in one transaction, so this whole method may be retried as often as the provider
        // retries its webhook and the account is credited exactly once.
        return await grainFactory
            .GetPlayerWalletGrain(player)
            .CreditOnceAsync(
                [new WalletDebitRequest { CurrencyKind = currency, Amount = order.Amount }],
                operation,
                CommerceStepKeys.SHOP_GRANT,
                ct
            )
            .ConfigureAwait(false);
    }

    private bool TryResolveConfiguredProvider(out IShopPaymentProvider provider)
    {
        provider = null!;

        return _providers.TryGetValue(_config.Provider ?? string.Empty, out provider!)
            && TryGetSecret(provider.Key, out _);
    }

    /// <summary>
    /// The provider's signing secret, or false when it has none or one too short to be worth having.
    /// </summary>
    private bool TryGetSecret(string providerKey, out string secret)
    {
        secret = string.Empty;

        if (
            _config.ProviderSecrets is null
            || !_config.ProviderSecrets.TryGetValue(providerKey, out string? configured)
            || configured is null
            || configured.Length < ShopConfig.MinimumSecretLength
        )
        {
            return false;
        }

        secret = configured;

        return true;
    }

    private static CurrencyType ToCurrencyType(ShopProductKind kind) =>
        kind switch
        {
            ShopProductKind.Duckets => CurrencyType.Silver,
            ShopProductKind.Diamonds => CurrencyType.Emeralds,
            _ => CurrencyType.Credits,
        };

    private static ShopProduct ToProduct(ShopProductEntity entity) =>
        new(
            entity.Code,
            entity.Kind,
            entity.Amount,
            entity.PriceMinor,
            entity.Currency,
            entity.Section,
            entity.Icon,
            entity.Featured
        );

    private static ShopOrder ToOrder(ShopOrderEntity entity) =>
        new(
            entity.PublicId.ToString(),
            entity.ProductCode,
            entity.Kind,
            entity.Amount,
            entity.PriceMinor,
            entity.Currency,
            entity.State,
            entity.Provider,
            entity.CreatedAt
        );
}

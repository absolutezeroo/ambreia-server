using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Database.Entities.Shop;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Shop;
using Xunit;

namespace Vortex.Shop.Tests;

/// <summary>
/// What the shop must not do. Each of these is a way a hotel's shop has actually been robbed, and
/// the assertion is the thing that stops it.
/// </summary>
public sealed class ShopServiceTests
{
    [Fact]
    public async Task StartOrder_snapshots_the_price_from_the_product()
    {
        await using ShopTestHarness harness = new();
        await harness.SeedProductAsync("c-100", ShopProductKind.Credits, 100, 450);

        ShopOrderResult result = await harness.Service.StartOrderAsync(
            ShopTestHarness.PlayerId,
            "c-100",
            CancellationToken.None
        );

        result.Start.Should().NotBeNull();
        result.Start!.Order.PriceMinor.Should().Be(450);
        result.Start.Order.Amount.Should().Be(100);
        result.Start.Order.State.Should().Be(ShopOrderState.Pending);

        // The manual provider hosts no page. Null is the honest answer, not a URL to nowhere.
        result.Start.RedirectUrl.Should().BeNull();
    }

    [Fact]
    public async Task StartOrder_refuses_a_product_that_is_not_on_sale()
    {
        await using ShopTestHarness harness = new();
        ShopProductEntity product = await harness.SeedProductAsync(
            "c-100",
            ShopProductKind.Credits,
            100,
            450
        );

        _ = product;

        ShopOrderResult result = await harness.Service.StartOrderAsync(
            ShopTestHarness.PlayerId,
            "c-999",
            CancellationToken.None
        );

        result.Start.Should().BeNull();
        result.Refusal.Should().Be(ShopOrderRefusal.UnknownProduct);
    }

    [Fact]
    public async Task StartOrder_refuses_when_no_provider_secret_is_configured()
    {
        // The fail-closed default. A shop with no signing key must not be able to open an order it
        // could never verify the payment of.
        await using ShopTestHarness harness = new(config => config.ProviderSecrets.Clear());
        await harness.SeedProductAsync("c-100", ShopProductKind.Credits, 100, 450);

        ShopOrderResult result = await harness.Service.StartOrderAsync(
            ShopTestHarness.PlayerId,
            "c-100",
            CancellationToken.None
        );

        result.Refusal.Should().Be(ShopOrderRefusal.Unavailable);
    }

    [Fact]
    public async Task Webhook_grants_the_order_amount_and_marks_it_fulfilled()
    {
        await using ShopTestHarness harness = new();
        await harness.SeedProductAsync("c-100", ShopProductKind.Credits, 100, 450);

        ShopOrder order = await OpenAsync(harness, "c-100");

        ShopWebhookOutcome outcome = await harness.Service.HandleWebhookAsync(
            ShopTestHarness.Notify(order.Id, 450),
            CancellationToken.None
        );

        outcome.Should().Be(ShopWebhookOutcome.Accepted);
        harness.Wallet.Credits.Should().ContainSingle();
        harness.Wallet.Credits[0].Amount.Should().Be(100);
        harness.Wallet.Credits[0].CurrencyKind.CurrencyType.Should().Be(CurrencyType.Credits);

        ShopOrderEntity? row = await harness.ReadOrderAsync(order.Id);
        row!.State.Should().Be(ShopOrderState.Fulfilled);
        row.PaidAt.Should().NotBeNull();

        // Opened past its pivot, because the money was captured before the hotel was told.
        harness.Journal.Transitions.Should().Contain(CommerceOperationState.Pivoted);
        harness.Journal.Transitions.Should().Contain(CommerceOperationState.Completed);
    }

    [Fact]
    public async Task Webhook_delivered_twice_grants_once()
    {
        await using ShopTestHarness harness = new();
        await harness.SeedProductAsync("c-100", ShopProductKind.Credits, 100, 450);

        ShopOrder order = await OpenAsync(harness, "c-100");
        ShopWebhookRequest notification = ShopTestHarness.Notify(order.Id, 450);

        await harness.Service.HandleWebhookAsync(notification, CancellationToken.None);

        // Byte for byte the same message. Every provider retries, and a retry is not a second sale.
        ShopWebhookOutcome second = await harness.Service.HandleWebhookAsync(
            notification,
            CancellationToken.None
        );

        second.Should().Be(ShopWebhookOutcome.Accepted);
        harness.Wallet.Credits.Should().ContainSingle();
    }

    [Fact]
    public async Task Webhook_paying_less_than_the_order_grants_nothing()
    {
        await using ShopTestHarness harness = new();
        await harness.SeedProductAsync("c-1000", ShopProductKind.Credits, 1000, 2950);

        ShopOrder order = await OpenAsync(harness, "c-1000");

        // One cent for a thousand credits.
        ShopWebhookOutcome outcome = await harness.Service.HandleWebhookAsync(
            ShopTestHarness.Notify(order.Id, 1),
            CancellationToken.None
        );

        outcome.Should().Be(ShopWebhookOutcome.AmountMismatch);
        harness.Wallet.Credits.Should().BeEmpty();

        ShopOrderEntity? row = await harness.ReadOrderAsync(order.Id);
        row!.State.Should().Be(ShopOrderState.NeedsIntervention);
    }

    [Fact]
    public async Task Webhook_in_another_currency_grants_nothing()
    {
        await using ShopTestHarness harness = new();
        await harness.SeedProductAsync("c-100", ShopProductKind.Credits, 100, 450);

        ShopOrder order = await OpenAsync(harness, "c-100");

        // 450 of something is not 450 euros.
        ShopWebhookOutcome outcome = await harness.Service.HandleWebhookAsync(
            ShopTestHarness.Notify(order.Id, 450, currency: "XOF"),
            CancellationToken.None
        );

        outcome.Should().Be(ShopWebhookOutcome.AmountMismatch);
        harness.Wallet.Credits.Should().BeEmpty();
    }

    [Fact]
    public async Task Webhook_with_no_signature_grants_nothing()
    {
        await using ShopTestHarness harness = new();
        await harness.SeedProductAsync("c-100", ShopProductKind.Credits, 100, 450);

        ShopOrder order = await OpenAsync(harness, "c-100");

        ShopWebhookRequest unsigned = new(
            "manual",
            $$"""{"orderId":"{{order.Id}}","reference":"x","paidMinor":450,"currency":"EUR","paid":true}""",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        );

        ShopWebhookOutcome outcome = await harness.Service.HandleWebhookAsync(
            unsigned,
            CancellationToken.None
        );

        outcome.Should().Be(ShopWebhookOutcome.BadSignature);
        harness.Wallet.Credits.Should().BeEmpty();
    }

    [Fact]
    public async Task Webhook_signed_with_the_wrong_secret_grants_nothing()
    {
        await using ShopTestHarness harness = new();
        await harness.SeedProductAsync("c-100", ShopProductKind.Credits, 100, 450);

        ShopOrder order = await OpenAsync(harness, "c-100");

        ShopWebhookOutcome outcome = await harness.Service.HandleWebhookAsync(
            ShopTestHarness.Notify(
                order.Id,
                450,
                secret: "a-different-secret-that-is-also-long-enough"
            ),
            CancellationToken.None
        );

        outcome.Should().Be(ShopWebhookOutcome.BadSignature);
        harness.Wallet.Credits.Should().BeEmpty();
    }

    [Fact]
    public async Task Webhook_replayed_from_outside_the_tolerance_grants_nothing()
    {
        await using ShopTestHarness harness = new();
        await harness.SeedProductAsync("c-100", ShopProductKind.Credits, 100, 450);

        ShopOrder order = await OpenAsync(harness, "c-100");

        // A genuine notification, captured and replayed an hour later. The timestamp is inside the
        // signed string, so it cannot be updated without the secret.
        ShopWebhookOutcome outcome = await harness.Service.HandleWebhookAsync(
            ShopTestHarness.Notify(order.Id, 450, signedAt: DateTimeOffset.UtcNow.AddHours(-1)),
            CancellationToken.None
        );

        outcome.Should().Be(ShopWebhookOutcome.BadSignature);
        harness.Wallet.Credits.Should().BeEmpty();
    }

    [Fact]
    public async Task Webhook_whose_body_was_edited_after_signing_grants_nothing()
    {
        await using ShopTestHarness harness = new();
        await harness.SeedProductAsync("c-25", ShopProductKind.Credits, 25, 150);

        ShopOrder order = await OpenAsync(harness, "c-25");

        ShopWebhookRequest honest = ShopTestHarness.Notify(order.Id, 150);
        ShopWebhookRequest tampered = honest with
        {
            Body = honest.Body.Replace(
                "\"paidMinor\":150",
                "\"paidMinor\":1",
                StringComparison.Ordinal
            ),
        };

        ShopWebhookOutcome outcome = await harness.Service.HandleWebhookAsync(
            tampered,
            CancellationToken.None
        );

        outcome.Should().Be(ShopWebhookOutcome.BadSignature);
        harness.Wallet.Credits.Should().BeEmpty();
    }

    [Fact]
    public async Task A_grant_that_does_not_land_leaves_the_order_owed()
    {
        await using ShopTestHarness harness = new();
        await harness.SeedProductAsync("c-100", ShopProductKind.Credits, 100, 450);

        ShopOrder order = await OpenAsync(harness, "c-100");
        harness.Wallet.Refuse = true;

        ShopWebhookOutcome outcome = await harness.Service.HandleWebhookAsync(
            ShopTestHarness.Notify(order.Id, 450),
            CancellationToken.None
        );

        // Accepted: the provider is told the notification arrived, because it did, and retrying it
        // would not make the wallet work. The order is what carries the problem.
        outcome.Should().Be(ShopWebhookOutcome.Accepted);

        ShopOrderEntity? row = await harness.ReadOrderAsync(order.Id);
        row!.State.Should().Be(ShopOrderState.NeedsIntervention);
        row.PaidAt.Should().NotBeNull();

        // Never Completed, so the operation stays past its pivot and the existing alert finds it.
        harness.Journal.Transitions.Should().NotContain(CommerceOperationState.Completed);
    }

    [Fact]
    public async Task An_unpaid_notification_cancels_the_order_and_grants_nothing()
    {
        await using ShopTestHarness harness = new();
        await harness.SeedProductAsync("c-100", ShopProductKind.Credits, 100, 450);

        ShopOrder order = await OpenAsync(harness, "c-100");

        await harness.Service.HandleWebhookAsync(
            ShopTestHarness.Notify(order.Id, 450, paid: false),
            CancellationToken.None
        );

        ShopOrderEntity? row = await harness.ReadOrderAsync(order.Id);
        row!.State.Should().Be(ShopOrderState.Cancelled);
        harness.Wallet.Credits.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unpaid_notification_cannot_take_back_a_fulfilled_order()
    {
        await using ShopTestHarness harness = new();
        await harness.SeedProductAsync("c-100", ShopProductKind.Credits, 100, 450);

        ShopOrder order = await OpenAsync(harness, "c-100");

        await harness.Service.HandleWebhookAsync(
            ShopTestHarness.Notify(order.Id, 450),
            CancellationToken.None
        );

        // A refund or a chargeback. Whether the hotel takes the credits back is a policy decision
        // with a human behind it, not something a webhook handler performs on its own.
        await harness.Service.HandleWebhookAsync(
            ShopTestHarness.Notify(order.Id, 450, paid: false, reference: "pay_2"),
            CancellationToken.None
        );

        ShopOrderEntity? row = await harness.ReadOrderAsync(order.Id);
        row!.State.Should().Be(ShopOrderState.Fulfilled);
    }

    [Fact]
    public async Task Membership_months_are_granted_once_across_a_replayed_notification()
    {
        await using ShopTestHarness harness = new();
        await harness.SeedProductAsync("hc-3", ShopProductKind.MembershipVip, 3, 1450);

        ShopOrder order = await OpenAsync(harness, "hc-3");
        ShopWebhookRequest notification = ShopTestHarness.Notify(order.Id, 1450);

        await harness.Service.HandleWebhookAsync(notification, CancellationToken.None);
        await harness.Service.HandleWebhookAsync(notification, CancellationToken.None);

        harness.Membership.Calls.Should().Be(1);
        harness.Membership.Months.Should().Be(3);
        harness.Membership.Vip.Should().BeTrue();
    }

    [Fact]
    public async Task An_order_belongs_to_its_owner_and_nobody_else()
    {
        await using ShopTestHarness harness = new();
        await harness.SeedProductAsync("c-100", ShopProductKind.Credits, 100, 450);

        ShopOrder order = await OpenAsync(harness, "c-100");

        ShopOrder? stranger = await harness.Service.GetOrderAsync(
            ShopTestHarness.PlayerId + 1,
            order.Id,
            CancellationToken.None
        );

        // Indistinguishable from an order that does not exist: an id is not a capability.
        stranger.Should().BeNull();
    }

    [Fact]
    public async Task Too_many_open_orders_are_refused()
    {
        await using ShopTestHarness harness = new(config => config.MaximumOpenOrders = 2);
        await harness.SeedProductAsync("c-100", ShopProductKind.Credits, 100, 450);

        await OpenAsync(harness, "c-100");
        await OpenAsync(harness, "c-100");

        ShopOrderResult third = await harness.Service.StartOrderAsync(
            ShopTestHarness.PlayerId,
            "c-100",
            CancellationToken.None
        );

        third.Refusal.Should().Be(ShopOrderRefusal.TooManyOpen);
    }

    [Fact]
    public async Task A_webhook_for_an_unknown_provider_reads_nothing()
    {
        await using ShopTestHarness harness = new();

        ShopWebhookRequest request = ShopTestHarness.Notify(Guid.NewGuid().ToString(), 450) with
        {
            Provider = "stripe",
        };

        ShopWebhookOutcome outcome = await harness.Service.HandleWebhookAsync(
            request,
            CancellationToken.None
        );

        outcome.Should().Be(ShopWebhookOutcome.UnknownProvider);
    }

    [Fact]
    public async Task The_catalogue_hides_a_withdrawn_product()
    {
        await using ShopTestHarness harness = new();
        await harness.SeedProductAsync("c-100", ShopProductKind.Credits, 100, 450);

        ShopCatalog before = await harness.Service.GetCatalogAsync(CancellationToken.None);
        before.Sections.Should().ContainSingle();
        before.Sections[0].Products.Should().ContainSingle();

        await harness.WithdrawAsync("c-100");

        ShopCatalog after = await harness.Service.GetCatalogAsync(CancellationToken.None);
        after.Sections.Should().BeEmpty();
    }

    private static async Task<ShopOrder> OpenAsync(ShopTestHarness harness, string code)
    {
        ShopOrderResult result = await harness.Service.StartOrderAsync(
            ShopTestHarness.PlayerId,
            code,
            CancellationToken.None
        );

        result.Start.Should().NotBeNull();

        return result.Start!.Order;
    }
}

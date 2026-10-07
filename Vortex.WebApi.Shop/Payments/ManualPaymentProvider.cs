using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Shop;

namespace Vortex.WebApi.Shop.Payments;

/// <summary>
/// The provider for a hotel that has no payment contract yet: an operator reads the order, collects
/// the money however they actually collect it, and posts a signed notification to say so.
/// </summary>
/// <remarks>
/// <para>
/// It exists so the shop is FINISHED before any PSP is signed. Every part that is hard to get right —
/// the price snapshot, the signature, the replay guard, the deterministic operation id, the owed-past-
/// the-pivot grant — runs identically here and under a real provider, so the first real integration
/// is one class implementing three members and not a rewrite of the shop.
/// </para>
/// <para>
/// It hosts no payment page, so <see cref="StartAsync"/> answers null and the site shows the order as
/// pending. That is honest: nothing has been charged, and nothing pretends to have been.
/// </para>
/// <para>
/// Its notification is the generic shape, which makes it the format a dashboard button or a curl
/// command uses. That is not a weakness — it is signed with the same secret as everything else, and
/// an operator who holds that secret is already allowed to hand out credits.
/// </para>
/// </remarks>
internal sealed class ManualPaymentProvider : IShopPaymentProvider
{
    public string Key => "manual";

    public Task<string?> StartAsync(ShopOrder order, CancellationToken ct) =>
        Task.FromResult<string?>(null);

    public bool TryReadNotification(
        ShopWebhookRequest request,
        out ShopPaymentNotification notification
    )
    {
        notification = null!;

        if (request is null)
        {
            return false;
        }

        ManualNotification? body;

        try
        {
            body = JsonSerializer.Deserialize<ManualNotification>(request.Body, JsonOptions);
        }
        catch (JsonException)
        {
            // A body that is not JSON at all. Already past the signature check, so this is a
            // configuration or version mistake rather than an attack — either way there is nothing
            // to act on.
            return false;
        }

        if (
            body is null
            || string.IsNullOrWhiteSpace(body.OrderId)
            || string.IsNullOrWhiteSpace(body.Reference)
            || string.IsNullOrWhiteSpace(body.Currency)
            || body.PaidMinor < 0
        )
        {
            return false;
        }

        notification = new ShopPaymentNotification(
            body.OrderId,
            body.Reference,
            body.PaidMinor,
            body.Currency,
            body.Paid
        );

        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// <c>{"orderId":"…","reference":"…","paidMinor":450,"currency":"EUR","paid":true}</c>.
    /// <c>paid: false</c> is how an operator cancels an order that will never be paid.
    /// </summary>
    private sealed record ManualNotification(
        string? OrderId,
        string? Reference,
        int PaidMinor,
        string? Currency,
        bool Paid
    );
}

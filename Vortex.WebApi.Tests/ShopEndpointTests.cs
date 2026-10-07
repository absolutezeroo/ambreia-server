using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Shop;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// The shop's routes: who may call each one, and what a browser can and cannot make happen.
/// </summary>
/// <remarks>
/// The rule these are all about is that the catalogue and the webhook are anonymous, the orders
/// belong to a session, and NOTHING reachable from a browser can move an order to paid. The
/// verification itself is <c>Vortex.WebApi.Shop.Tests</c>' subject; here the question is only which door
/// each request goes through.
/// </remarks>
public sealed class ShopEndpointTests
{
    [Fact]
    public async Task Products_AreReadableSignedOut()
    {
        // The store page renders for a visitor who has not signed in — habbo.com's does too, and a
        // shop nobody can look at until they have an account sells nothing.
        await using WebApiTestFactory factory = new();

        HttpResponseMessage response = await factory.Client.GetAsync("/api/public/shop/products");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement catalog = await ReadJsonAsync(response);
        catalog
            .GetProperty("sections")[0]
            .GetProperty("products")[0]
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(FakeShopService.ProductCode);
    }

    [Fact]
    public async Task StartingAnOrder_NeedsASession()
    {
        await using WebApiTestFactory factory = new();

        HttpResponseMessage response = await factory.Client.PostAsJsonAsync(
            "/api/user/shop/orders",
            new { productCode = FakeShopService.ProductCode }
        );

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        factory.Shop.Started.Should().BeEmpty();
    }

    [Fact]
    public async Task StartingAnOrder_PassesOnlyTheProductCode()
    {
        await using WebApiTestFactory factory = new();
        HttpClient client = factory.CreateAuthenticatedClient();

        // A price and an amount are sent along, exactly as a tampering client would. There is no
        // field on the request record to bind them to, so they go nowhere: the service is called
        // with a code and reads the price itself.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/user/shop/orders",
            new
            {
                productCode = FakeShopService.ProductCode,
                priceMinor = 1,
                amount = 999999,
            }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory
            .Shop.Started.Should()
            .ContainSingle()
            .Which.Should()
            .Be(FakeShopService.ProductCode);

        JsonElement start = await ReadJsonAsync(response);
        start
            .GetProperty("order")
            .GetProperty("priceMinor")
            .GetInt32()
            .Should()
            .Be(FakeShopService.PriceMinor);
        start.GetProperty("order").GetProperty("amount").GetInt32().Should().Be(100);
        start.GetProperty("redirectUrl").GetString().Should().Be("https://pay.test/1");
    }

    [Fact]
    public async Task StartingAnOrder_RefusesAnUnknownProduct()
    {
        await using WebApiTestFactory factory = new();
        HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/user/shop/orders",
            new { productCode = "not-a-product" }
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(response))
            .GetProperty("error")
            .GetString()
            .Should()
            .Be("unknown_product");
    }

    [Fact]
    public async Task ReadingAnOrder_NeedsASession()
    {
        await using WebApiTestFactory factory = new();

        HttpResponseMessage response = await factory.Client.GetAsync(
            "/api/user/shop/orders/00000000-0000-0000-0000-000000000001"
        );

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReadingAnOrderThatIsNotYours_Is404()
    {
        await using WebApiTestFactory factory = new();
        HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync(
            "/api/user/shop/orders/00000000-0000-0000-0000-000000000001"
        );

        // Not a 403: telling a caller that an order exists but is someone else's is a way to
        // enumerate them.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheWebhook_IsAnonymousAndHandsTheBodyOverUnparsed()
    {
        await using WebApiTestFactory factory = new();

        // Deliberately odd whitespace and key order: the signature is over these bytes, so anything
        // that reserialises the body on the way in breaks every real provider's signature.
        const string Body = """{ "paid" : true,   "orderId":"x" }""";

        HttpResponseMessage response = await PostRawAsync(
            factory.Client,
            "/api/public/shop/webhook/manual",
            Body
        );

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        factory.Shop.LastWebhookBody.Should().Be(Body);
    }

    [Fact]
    public async Task TheWebhook_AnswersUnauthorizedOnABadSignature()
    {
        await using WebApiTestFactory factory = new();
        factory.Shop.WebhookOutcome = ShopWebhookOutcome.BadSignature;

        HttpResponseMessage response = await PostRawAsync(
            factory.Client,
            "/api/public/shop/webhook/manual",
            "{}"
        );

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheWebhook_AnswersNotFoundForAnUnknownProvider()
    {
        await using WebApiTestFactory factory = new();
        factory.Shop.WebhookOutcome = ShopWebhookOutcome.UnknownProvider;

        HttpResponseMessage response = await PostRawAsync(
            factory.Client,
            "/api/public/shop/webhook/stripe",
            "{}"
        );

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A POST whose body is exactly the string given, with no serializer in the way — which is the
    /// only way to assert that the webhook route hands the bytes on unchanged.
    /// </summary>
    private static async Task<HttpResponseMessage> PostRawAsync(
        HttpClient client,
        string url,
        string body
    )
    {
        using StringContent content = new(body, Encoding.UTF8, "application/json");

        return await client.PostAsync(url, content);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}

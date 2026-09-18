using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Database.Context;
using Vortex.Database.Entities.Collectibles;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// The document every wallet reads about a Relic.
/// </summary>
/// <remarks>
/// The field names are the point. This schema belongs to ERC-721 and not to this hotel, while the
/// API serialises camelCase by default — <c>trait_type</c> shipped as <c>traitType</c> is accepted
/// by every parser and understood by none, so the token renders blank and nothing anywhere throws.
/// Asserting on the raw property names is the only way that failure is visible.
/// </remarks>
public sealed class CollectibleMetadataEndpointTests
{
    [Fact]
    public async Task Metadata_UsesTheSchemasOwnFieldNames()
    {
        await using WebApiTestFactory factory = new();
        await SeedRelicAsync(factory, id: 90714, productCode: "dragonlamp", serial: 3, edition: 5);

        JsonElement body = await GetAsync(factory, 90714);

        body.TryGetProperty("name", out _).Should().BeTrue();
        body.TryGetProperty("description", out _).Should().BeTrue();
        body.TryGetProperty("attributes", out JsonElement attributes).Should().BeTrue();

        JsonElement first = attributes.EnumerateArray().First();

        first
            .TryGetProperty("trait_type", out _)
            .Should()
            .BeTrue("the schema spells it with an underscore");
        first
            .TryGetProperty("traitType", out _)
            .Should()
            .BeFalse("camelCase is what a wallet ignores");
    }

    /// <summary>
    /// The serial and the cap are what make one Relic different from another of the same furni, so
    /// they belong in the name a wallet prints rather than only in the attributes it may not show.
    /// </summary>
    [Fact]
    public async Task Metadata_NamesTheEdition()
    {
        await using WebApiTestFactory factory = new();
        await SeedRelicAsync(factory, id: 90714, productCode: "dragonlamp", serial: 3, edition: 5);

        JsonElement body = await GetAsync(factory, 90714);

        body.GetProperty("name").GetString().Should().Be("dragonlamp #3 of 5");
        Traits(body).Should().Contain("Edition size");
    }

    /// <summary>
    /// An open edition has no cap, and "3 of 0" is worse than saying nothing: the attribute is
    /// absent and the name carries the serial alone.
    /// </summary>
    [Fact]
    public async Task Metadata_SaysNothingAboutACapAnOpenEditionDoesNotHave()
    {
        await using WebApiTestFactory factory = new();
        await SeedRelicAsync(factory, id: 4, productCode: "throne", serial: 12, edition: 0);

        JsonElement body = await GetAsync(factory, 4);

        body.GetProperty("name").GetString().Should().Be("throne #12");
        Traits(body).Should().NotContain("Edition size");
    }

    /// <summary>
    /// With no asset host configured the image is absent rather than a relative path. The caller is
    /// software that has never heard of this hotel and cannot resolve one — a token with no picture
    /// beats a token with a broken one.
    /// </summary>
    [Fact]
    public async Task Metadata_OmitsTheImageWhenNoAssetHostIsConfigured()
    {
        await using WebApiTestFactory factory = new();
        await SeedRelicAsync(factory, id: 7, productCode: "dragonlamp", serial: 1, edition: 0);

        JsonElement body = await GetAsync(factory, 7);

        body.GetProperty("image").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// The link back to the hotel, which is the only address a Relic has outside the game.
    /// </summary>
    /// <remarks>
    /// The <c>#</c> is the assertion worth having: the site routes on the fragment, so the same URL
    /// without it is a path its server has no route for, and the failure would only ever be seen by
    /// somebody clicking through from a marketplace.
    /// </remarks>
    [Fact]
    public async Task Metadata_LinksBackToTheRelicsPageOnTheSite()
    {
        await using WebApiTestFactory factory = new(config =>
            // With the trailing slash on purpose: it is how anybody would write a base URL, and it
            // is what puts `//#/` in the link if nothing trims it.
            config.PublicSiteUrl = "https://vortex-hotel.online/"
        );
        await SeedRelicAsync(factory, id: 90714, productCode: "dragonlamp", serial: 3, edition: 5);

        JsonElement body = await GetAsync(factory, 90714);

        body.GetProperty("external_url")
            .GetString()
            .Should()
            .Be("https://vortex-hotel.online/#/habbo-nft/relic/90714");
    }

    /// <summary>
    /// With no site URL configured the field is absent rather than empty: a document with no link is
    /// one a wallet renders without a button, where an empty one renders a button that goes nowhere.
    /// </summary>
    [Fact]
    public async Task Metadata_OmitsTheLinkHomeWhenNoSiteUrlIsConfigured()
    {
        await using WebApiTestFactory factory = new();
        await SeedRelicAsync(factory, id: 11, productCode: "throne", serial: 1, edition: 0);

        JsonElement body = await GetAsync(factory, 11);

        body.TryGetProperty("external_url", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Metadata_IsNotFoundForAnAssetThatDoesNotExist()
    {
        await using WebApiTestFactory factory = new();

        using HttpResponseMessage response = await factory.Client.GetAsync(
            "/api/public/collectibles/999999"
        );

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<JsonElement> GetAsync(WebApiTestFactory factory, int assetId)
    {
        using HttpResponseMessage response = await factory.Client.GetAsync(
            $"/api/public/collectibles/{assetId}"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string[] Traits(JsonElement body) =>
        [
            .. body.GetProperty("attributes")
                .EnumerateArray()
                .Select(attribute =>
                    attribute.GetProperty("trait_type").GetString() ?? string.Empty
                ),
        ];

    private static async Task SeedRelicAsync(
        WebApiTestFactory factory,
        int id,
        string productCode,
        int serial,
        int edition
    )
    {
        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        db.NftAssets.Add(
            new NftAssetEntity
            {
                Id = id,
                PlayerEntityId = 1,
                ProductCode = productCode,
                SerialNumber = serial,
                EditionSize = edition,
            }
        );

        await db.SaveChangesAsync();
    }
}

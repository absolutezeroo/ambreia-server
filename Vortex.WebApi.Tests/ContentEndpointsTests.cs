using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Permissions;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>The content routes (spec Contenu §6, §7): import behind content.publish, texts public.</summary>
public sealed class ContentEndpointsTests
{
    private const string OnePack = """
        { "files": [{ "path": "class/warrior.json", "text": "{}" }], "migrations": null, "texts": { "fr": "" } }
        """;

    private const string EmptyPack = """{ "files": [], "migrations": null, "texts": {} }""";

    [Fact]
    public async Task WithoutASession_TheImportIsUnauthorized()
    {
        await using WebApiTestFactory f = new();
        using StringContent body = Json(OnePack);

        (await f.Client.PostAsync("/api/content/import", body))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithoutTheCapability_TheImportIsForbidden()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.World.ZonePublish]);

        (await PostAsync(f, OnePack)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnImportedPack_IsOk()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.Content.Publish]);

        HttpResponseMessage r = await PostAsync(f, OnePack);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync()).Should().Contain("\"packVersion\":1");
    }

    [Fact]
    public async Task ARefusedPack_IsABadRequestWithFindings()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.Content.Publish]);

        HttpResponseMessage r = await PostAsync(f, EmptyPack);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync())
            .Should()
            .Contain("\"code\":\"NoFrenchTexts\"")
            .And.Contain("\"severity\":\"Error\"");
    }

    [Fact]
    public async Task APackThatIsNotJson_IsABadRequest()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.Content.Publish]);

        (await PostAsync(f, "{ nope")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheTexts_AreServedAsPlainText()
    {
        await using WebApiTestFactory f = new();

        HttpResponseMessage r = await f.Client.GetAsync("/api/public/content/texts/fr");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        (await r.Content.ReadAsStringAsync()).Should().Be("class.warrior.name=Guerrier");
    }

    [Fact]
    public async Task AnUnknownLanguage_IsNotFound()
    {
        await using WebApiTestFactory f = new();

        (await f.Client.GetAsync("/api/public/content/texts/de"))
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<HttpResponseMessage> PostAsync(WebApiTestFactory f, string json)
    {
        using StringContent body = Json(json);

        return await f.CreateAuthenticatedClient().PostAsync("/api/content/import", body);
    }
}

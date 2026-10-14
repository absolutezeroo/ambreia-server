using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Permissions;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>Content authoring §4, §5: the content one document at a time, behind content.publish.</summary>
public sealed class ContentAuthoringEndpointsTests
{
    private const string Put = """
        { "baseVersion": 1, "kind": "item", "path": "item/sword.json",
          "source": "{ \"id\": \"item.weapon.sword\", \"kind\": \"item\" }",
          "texts": { "fr": { "item.weapon.sword.name": "Épée" } } }
        """;

    [Theory]
    [InlineData("GET", "/api/content/documents?kind=item")]
    [InlineData("GET", "/api/content/documents/item.weapon.sword")]
    [InlineData("PUT", "/api/content/documents/item.weapon.sword")]
    [InlineData("DELETE", "/api/content/documents/item.weapon.sword?baseVersion=1")]
    [InlineData("GET", "/api/content/export")]
    [InlineData("GET", "/api/content/schemas/item")]
    public async Task EveryRoute_RequiresContentPublish(string method, string path)
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.World.ZonePublish]);

        (await SendAsync(f, method, path, Put)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        f.Authoring.Puts.Should().BeEmpty();
        f.Authoring.Deletes.Should().BeEmpty();
    }

    [Fact]
    public async Task TheList_IsTheKindsDocuments_AndAnUnknownKindIsNotFound()
    {
        await using WebApiTestFactory f = Publisher();

        HttpResponseMessage list = await f.CreateAuthenticatedClient()
            .GetAsync("/api/content/documents?kind=item");

        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadAsStringAsync()).Should().Contain("\"path\":\"item/sword.json\"");
        (await f.CreateAuthenticatedClient().GetAsync("/api/content/documents?kind=spell"))
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ADocument_IsReadBackWithItsTexts_AndAnUnknownOneIsNotFound()
    {
        await using WebApiTestFactory f = Publisher();

        string body = await (
            await f.CreateAuthenticatedClient().GetAsync("/api/content/documents/item.weapon.sword")
        ).Content.ReadAsStringAsync();

        body.Should().Contain("\"version\":1").And.Contain("\"item.weapon.sword.name\":\"Épée\"");
        (await f.CreateAuthenticatedClient().GetAsync("/api/content/documents/item.weapon.ghost"))
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task APut_IsWrittenUnderTheUrlsId_AndAnswersTheNewVersion()
    {
        await using WebApiTestFactory f = Publisher();

        HttpResponseMessage r = await SendAsync(
            f,
            "PUT",
            "/api/content/documents/item.weapon.sword",
            Put
        );

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync()).Should().Contain("\"packVersion\":2");
        var write = f.Authoring.Puts.Single();
        (write.Id, write.Kind, write.BaseVersion, write.Path)
            .Should()
            .Be(("item.weapon.sword", "item", 1L, "item/sword.json"));
        write.Texts!["fr"]["item.weapon.sword.name"].Should().Be("Épée");
    }

    [Fact]
    public async Task AStalePut_IsAConflict()
    {
        await using WebApiTestFactory f = Publisher();

        HttpResponseMessage r = await SendAsync(
            f,
            "PUT",
            "/api/content/documents/item.weapon.sword",
            Put.Replace("\"baseVersion\": 1", "\"baseVersion\": 0")
        );

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await r.Content.ReadAsStringAsync()).Should().Contain("content.stale");
    }

    [Theory]
    [InlineData("content.id_mismatch")]
    [InlineData("content.bad_path")]
    [InlineData("content.bad_text")]
    [InlineData("content.bad_source")]
    [InlineData("content.unknown_kind")]
    public async Task AWriteTheEditRefuses_IsABadRequestWithItsCode(string code)
    {
        await using WebApiTestFactory f = Publisher();
        f.Authoring.NextError = code;

        HttpResponseMessage r = await SendAsync(
            f,
            "PUT",
            "/api/content/documents/item.weapon.sword",
            Put
        );

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain(code);
    }

    [Fact]
    public async Task AWriteTheValidatorRefuses_IsABadRequestWithItsFindings()
    {
        await using WebApiTestFactory f = Publisher();
        f.Authoring.NextRefusal = true;

        HttpResponseMessage r = await SendAsync(
            f,
            "PUT",
            "/api/content/documents/item.weapon.sword",
            Put
        );

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("\"code\":\"BrokenReference\"");
    }

    [Fact]
    public async Task APutOver256KB_IsTooLarge()
    {
        await using WebApiTestFactory f = Publisher();
        string big = Put.Replace("Épée", new string('a', 300 * 1024));

        (await SendAsync(f, "PUT", "/api/content/documents/item.weapon.sword", big))
            .StatusCode.Should()
            .Be(HttpStatusCode.RequestEntityTooLarge);
        f.Authoring.Puts.Should().BeEmpty();
    }

    [Fact]
    public async Task AWriteThatIsNotJson_IsUnsupported()
    {
        await using WebApiTestFactory f = Publisher();
        using StringContent form = new(
            "baseVersion=1",
            Encoding.UTF8,
            "application/x-www-form-urlencoded"
        );

        HttpResponseMessage r = await f.CreateAuthenticatedClient()
            .PutAsync("/api/content/documents/item.weapon.sword", form);

        r.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        f.Authoring.Puts.Should().BeEmpty();
    }

    [Fact]
    public async Task APutThatIsNoRequest_IsABadRequest()
    {
        await using WebApiTestFactory f = Publisher();

        (await SendAsync(f, "PUT", "/api/content/documents/item.weapon.sword", "{ nope"))
            .StatusCode.Should()
            .Be(HttpStatusCode.BadRequest);
        f.Authoring.Puts.Should().BeEmpty();
    }

    [Fact]
    public async Task ADelete_PassesItsVersionAndReplacement_AndAnUnknownDocumentIsNotFound()
    {
        await using WebApiTestFactory f = Publisher();

        (
            await f.CreateAuthenticatedClient()
                .DeleteAsync(
                    "/api/content/documents/item.weapon.sword?baseVersion=1&replaceWith=item.weapon.axe"
                )
        )
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        f.Authoring.Deletes.Should().Equal(("item.weapon.sword", 1L, "item.weapon.axe"));
        (
            await f.CreateAuthenticatedClient()
                .DeleteAsync("/api/content/documents/item.weapon.ghost?baseVersion=1")
        )
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AKindsSchema_IsServedAsJson_AndAnUnknownKindIsNotFound()
    {
        await using WebApiTestFactory f = Publisher();

        HttpResponseMessage r = await f.CreateAuthenticatedClient()
            .GetAsync("/api/content/schemas/item");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        (await r.Content.ReadAsStringAsync()).Should().Contain("\"category\"");
        (await f.CreateAuthenticatedClient().GetAsync("/api/content/schemas/spell"))
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheExport_IsTheStoredPack()
    {
        await using WebApiTestFactory f = Publisher();

        string body = await (
            await f.CreateAuthenticatedClient().GetAsync("/api/content/export")
        ).Content.ReadAsStringAsync();

        body.Should().Contain("\"version\":1").And.Contain("\"path\":\"item/sword.json\"");
    }

    [Fact]
    public async Task AnImportOnAStaleVersion_IsAConflict_UnlessForced()
    {
        await using WebApiTestFactory f = Publisher();
        const string pack = """
            { "files": [{ "path": "class/warrior.json", "text": "{}" }], "migrations": null, "texts": { "fr": "" } }
            """;

        (await SendAsync(f, "POST", "/api/content/import?baseVersion=5", pack))
            .StatusCode.Should()
            .Be(HttpStatusCode.Conflict);
        (await SendAsync(f, "POST", "/api/content/import?baseVersion=5&force=true", pack))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
    }

    private static WebApiTestFactory Publisher() =>
        new(capabilities: [Capabilities.Content.Publish]);

    private static async Task<HttpResponseMessage> SendAsync(
        WebApiTestFactory f,
        string method,
        string path,
        string json
    )
    {
        using HttpRequestMessage request = new(new HttpMethod(method), path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        return await f.CreateAuthenticatedClient().SendAsync(request);
    }
}

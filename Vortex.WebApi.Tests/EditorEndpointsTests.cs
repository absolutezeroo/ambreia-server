using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// The map editor's routes: a session, then a capability, then the service. Building is
/// world.zone.edit; making a version live for players is world.zone.publish.
/// </summary>
public sealed class EditorEndpointsTests
{
    [Fact]
    public async Task WithoutASession_TheEditorIsUnauthorized()
    {
        await using WebApiTestFactory f = new();

        (await f.Client.GetAsync("/api/editor/zones"))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithoutTheCapability_TheEditorIsForbidden()
    {
        await using WebApiTestFactory f = new(capabilities: []);

        (await f.CreateAuthenticatedClient().GetAsync("/api/editor/zones"))
            .StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
    }

    /// <summary>The gate is declared route by route: one left off is a route anyone can call.</summary>
    [Theory]
    [InlineData("GET", "/api/editor/zones")]
    [InlineData("POST", "/api/editor/zones")]
    [InlineData("GET", "/api/editor/zones/village/versions")]
    [InlineData("GET", "/api/editor/zones/village/document")]
    [InlineData("POST", "/api/editor/zones/village/validate")]
    [InlineData("POST", "/api/editor/zones/village/versions")]
    [InlineData("POST", "/api/editor/zones/village/publish/1")]
    [InlineData("POST", "/api/editor/zones/village/lock")]
    [InlineData("DELETE", "/api/editor/zones/village/lock")]
    [InlineData("POST", "/api/editor/zones/village/test")]
    [InlineData("GET", "/api/editor/furniture")]
    public async Task EveryEditorRoute_RefusesWithoutTheCapability(string method, string path)
    {
        await using WebApiTestFactory f = new(capabilities: []);

        using HttpRequestMessage request = new(new HttpMethod(method), path)
        {
            Content =
                method == "GET"
                    ? null
                    : new StringContent(
                        "{\"key\":\"k\",\"name\":\"n\"}",
                        Encoding.UTF8,
                        "application/json"
                    ),
        };

        (await f.CreateAuthenticatedClient().SendAsync(request))
            .StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WithTheEditCapability_TheZonesAreListed()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.World.ZoneEdit]);

        HttpResponseMessage r = await f.CreateAuthenticatedClient().GetAsync("/api/editor/zones");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync()).Should().Contain("\"key\":\"village\"");
    }

    [Fact]
    public async Task SavingGarbage_IsABadRequestWithFindings()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.World.ZoneEdit]);

        HttpResponseMessage r = await PostJsonAsync(
            f,
            "/api/editor/zones/village/versions",
            "{not json"
        );

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync())
            .Should()
            .Contain("\"code\":\"Json\"")
            .And.Contain("\"severity\":\"Error\"");
    }

    [Fact]
    public async Task TestMode_TicketsTheTesterIntoThePublishedZone()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.World.ZoneEdit]);

        HttpResponseMessage r = await f.CreateAuthenticatedClient()
            .PostAsync("/api/editor/zones/village/test?x=2&y=3", null);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync())
            .Should()
            .Contain("\"ssoToken\":\"ticket-100\"")
            .And.Contain("\"roomId\":7");
        f.ZoneTests.Should().Equal((new PlayerId(100), new RoomId(7), 2, 3));
    }

    [Fact]
    public async Task TestMode_ForAnUnknownZone_IsNotFound()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.World.ZoneEdit]);

        (await f.CreateAuthenticatedClient().PostAsync("/api/editor/zones/moon/test", null))
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PublishingNeedsThePublishCapability()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.World.ZoneEdit]);

        (await f.CreateAuthenticatedClient().PostAsync("/api/editor/zones/village/publish/1", null))
            .StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatingATakenKey_IsAConflict()
    {
        await using WebApiTestFactory f = new();

        HttpResponseMessage r = await PostJsonAsync(
            f,
            "/api/editor/zones",
            "{\"key\":\"village\",\"name\":\"Village\"}"
        );

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AnUnknownZone_HasNoDocument()
    {
        await using WebApiTestFactory f = new();

        (await f.CreateAuthenticatedClient().GetAsync("/api/editor/zones/moon/document"))
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ThePaletteListsFurniture()
    {
        await using WebApiTestFactory f = new();

        (
            await (
                await f.CreateAuthenticatedClient().GetAsync("/api/editor/furniture")
            ).Content.ReadAsStringAsync()
        )
            .Should()
            .Contain("\"class\":\"throne\"");
    }

    [Fact]
    public async Task SavingToAnUnknownZone_IsNotFound()
    {
        await using WebApiTestFactory f = new();

        (await PostJsonAsync(f, "/api/editor/zones/moon/versions", "{}"))
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ASaveThatLostTheRace_IsAConflict()
    {
        await using WebApiTestFactory f = new();

        (await PostJsonAsync(f, "/api/editor/zones/busy/versions", "{}"))
            .StatusCode.Should()
            .Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task TheLatestDraft_CanBeReopened()
    {
        await using WebApiTestFactory f = new();

        (
            await (
                await f.CreateAuthenticatedClient()
                    .GetAsync("/api/editor/zones/village/document?version=latest")
            ).Content.ReadAsStringAsync()
        )
            .Should()
            .Contain("\"draft\":true");
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(
        WebApiTestFactory f,
        string url,
        string json
    )
    {
        using StringContent body = new(json, Encoding.UTF8, "application/json");

        return await f.CreateAuthenticatedClient().PostAsync(url, body);
    }
}

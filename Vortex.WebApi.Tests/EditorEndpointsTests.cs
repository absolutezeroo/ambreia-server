using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Permissions;
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

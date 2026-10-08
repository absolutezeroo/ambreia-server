using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// Contract integration tests: one per migrated route, asserting the status code and response shape
/// are unchanged from the old <c>HttpListener</c> surface, plus a brute-force check that the login
/// rate-limiting policy returns 429 once the permit window is exhausted.
/// </summary>
public sealed class WebApiEndpointsTests
{
    [Fact]
    public async Task Hello_ReturnsOkStatus()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();

        HttpResponseMessage response = await factory.Client.GetAsync("/api/public/info/hello");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(response)).GetProperty("status").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task Health_WithDatabaseReachableAndNotDegraded_ReturnsHealthy()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();

        HttpResponseMessage response = await factory.Client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("status").GetString().Should().Be("Healthy");
        body.GetProperty("database").GetString().Should().Be("up");
    }

    [Fact]
    public async Task Login_WithValidCredentials_Returns200AndIssuesCookie()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();

        HttpResponseMessage response = await factory.Client.PostAsJsonAsync(
            "/api/public/authentication/login",
            new { email = FakeAuthService.ValidEmail, password = FakeAuthService.ValidPassword }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response
            .Headers.GetValues("Set-Cookie")
            .Should()
            .Contain(value => value.Contains("habbo-web-session="));
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();

        HttpResponseMessage response = await factory.Client.PostAsJsonAsync(
            "/api/public/authentication/login",
            new { email = FakeAuthService.ValidEmail, password = "wrong" }
        );

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadJsonAsync(response)).GetProperty("error").GetString().Should().NotBeNull();
    }

    [Fact]
    public async Task Login_WithMissingFields_Returns400()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();

        HttpResponseMessage response = await factory.Client.PostAsJsonAsync(
            "/api/public/authentication/login",
            new { email = "", password = "" }
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(response))
            .GetProperty("error")
            .GetString()
            .Should()
            .Be("pocket.auth.missing_credentials");
    }

    [Fact]
    public async Task Logout_Returns200()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();

        HttpResponseMessage response = await factory.Client.PostAsync(
            "/api/public/authentication/logout",
            content: null
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_NewAccount_Returns200WithId()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();

        HttpResponseMessage response = await factory.Client.PostAsJsonAsync(
            "/api/public/registration/new",
            new { email = "new@test.com", password = "secret-pass" }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(response)).GetProperty("id").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetAvatars_Unauthenticated_Returns401()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();

        HttpResponseMessage response = await factory.Client.GetAsync("/api/user/avatars");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAvatars_Authenticated_ReturnsList()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync("/api/user/avatars");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement payload = await ReadJsonAsync(response);
        payload.ValueKind.Should().Be(JsonValueKind.Array);
        payload
            .EnumerateArray()
            .First()
            .GetProperty("uniqueId")
            .GetString()
            .Should()
            .Be(FakePlayerService.OwnedUniqueId);
    }

    [Fact]
    public async Task CreateAvatar_Authenticated_Returns200WithList()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/user/avatars",
            new
            {
                name = "Newbie",
                figure = "hd-180-1",
                gender = "M",
                // A named avatar takes its class at creation (spec Personnage §8).
                classId = "class.warrior",
            }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(response)).ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task SelectAvatar_Authenticated_Returns200()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/user/avatars/select",
            new { uniqueId = FakePlayerService.OwnedUniqueId }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SsoToken_Authenticated_ReturnsTicket()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync("/api/ssotoken");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(response))
            .GetProperty("ssoToken")
            .GetString()
            .Should()
            .NotBeNullOrEmpty();
    }

    /// <summary>Spec F3a §5.1: without a selected avatar, the ticket names the account — the client
    /// then picks its character on the socket.</summary>
    [Fact]
    public async Task SsoToken_WithoutASelectedAvatar_IsAnAccountTicket()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync("/api/ssotoken");

        (await ReadJsonAsync(response))
            .GetProperty("ssoToken")
            .GetString()
            .Should()
            .StartWith("account-ticket-");
    }

    [Fact]
    public async Task SsoToken_ForASelectedAvatar_IsStillAPlayerTicket()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/ssotoken?uniqueId={FakePlayerService.OwnedUniqueId}"
        );

        (await ReadJsonAsync(response))
            .GetProperty("ssoToken")
            .GetString()
            .Should()
            .Be($"ticket-{FakePlayerService.OwnedUniqueId}");
    }

    [Fact]
    public async Task NameCheck_Returns200WithValidity()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();

        HttpResponseMessage response = await factory.Client.PostAsJsonAsync(
            "/api/newuser/name/check",
            new { name = "Available" }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement payload = await ReadJsonAsync(response);
        payload.GetProperty("name").GetString().Should().Be("Available");
        payload.GetProperty("valid").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task NameSelect_Authenticated_Returns200()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/newuser/name/select",
            new { name = "Chosen", playerId = 100 }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(response)).GetProperty("name").GetString().Should().Be("Chosen");
    }

    [Fact]
    public async Task Login_ExceedingRateLimit_Returns429()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();

        List<HttpStatusCode> statuses = new List<HttpStatusCode>();

        for (int attempt = 0; attempt < WebApiTestFactory.LoginPermitLimit + 2; attempt++)
        {
            HttpResponseMessage response = await factory.Client.PostAsJsonAsync(
                "/api/public/authentication/login",
                new { email = FakeAuthService.ValidEmail, password = "wrong" }
            );

            statuses.Add(response.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(body).RootElement.Clone();
    }
}

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

    /// <summary>Liveness answers for the process alone: a hotel still starting, or one whose
    /// database is away, is alive and must not be restarted for it.</summary>
    [Fact]
    public async Task Health_IsLiveness_AnswersHealthyBeforeTheHostIsReady_WithoutTheDatabase()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();

        HttpResponseMessage response = await factory.Client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("status").GetString().Should().Be("Healthy");
        body.TryGetProperty("database", out _)
            .Should()
            .BeFalse("liveness does not ask the database");
    }

    [Fact]
    public async Task Ready_BeforeTheHostIsReady_Answers503_AndSaysWhy()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();

        HttpResponseMessage response = await factory.Client.GetAsync("/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("status").GetString().Should().Be("NotReady");
        body.GetProperty("blocker").GetString().Should().Be("starting");
    }

    [Fact]
    public async Task Ready_OnceTheListenersAreOpen_Answers200_WithTheDatabaseUp()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();
        factory.Readiness.MarkReady();

        HttpResponseMessage response = await factory.Client.GetAsync("/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("status").GetString().Should().Be("Ready");
        body.GetProperty("database").GetString().Should().Be("up");
    }

    [Fact]
    public async Task Ready_WhileTheHostDrains_Answers503()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();
        factory.Readiness.MarkReady();
        factory.Readiness.MarkStopping();

        HttpResponseMessage response = await factory.Client.GetAsync("/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ReadJsonAsync(response)).GetProperty("blocker").GetString().Should().Be("stopping");
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

    /// <summary>Spec F3a §5.1: the ticket names the account — the client then picks its character
    /// on the socket.</summary>
    [Fact]
    public async Task SsoToken_IsAnAccountTicket()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync("/api/ssotoken");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(response))
            .GetProperty("ssoToken")
            .GetString()
            .Should()
            .Be($"account-ticket-{FakeAuthService.AccountId}");
    }

    [Fact]
    public async Task SsoToken_WithoutASession_Returns401()
    {
        await using WebApiTestFactory factory = new WebApiTestFactory();

        HttpResponseMessage response = await factory.Client.GetAsync("/api/ssotoken");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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

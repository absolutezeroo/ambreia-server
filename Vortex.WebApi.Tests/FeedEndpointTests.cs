using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Feed;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Vortex.WebApi.Services;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>The feed's routes, through the real routing pipeline.</summary>
public sealed class FeedEndpointTests
{
    [Fact]
    public async Task Feed_IsAnonymous()
    {
        await using WebApiTestFactory factory = new();

        using HttpResponseMessage response = await factory.Client.GetAsync("/api/public/feed");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement page = JsonDocument
            .Parse(await response.Content.ReadAsStringAsync())
            .RootElement;

        page.GetProperty("total").GetInt32().Should().Be(0);
        page.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Post_AnswersNotFoundWithACode()
    {
        await using WebApiTestFactory factory = new();

        using HttpResponseMessage response = await factory.Client.GetAsync("/api/public/feed/404");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        JsonDocument
            .Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("error")
            .GetString()
            .Should()
            .Be("feed_post_not_found");
    }

    /// <summary>An account with no second factor is asked for nothing: the delete just happens.</summary>
    [Fact]
    public async Task Delete_AsksNothingOfAnAccountWithoutASecondFactor()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory);

        FeedPost post = await StatusAsync(factory);

        using HttpClient client = factory.CreateAuthenticatedClient();
        using HttpResponseMessage response = await client.DeleteAsync($"/api/user/feed/{post.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Service(factory).GetPostAsync(post.Id, null, default)).Should().BeNull();
    }

    /// <summary>
    /// Deleting is the one thing on the feed that cannot be undone, so an account that has a second
    /// factor has to produce one. Three answers in a row, because the middle one is the whole
    /// point: an attacker holding the session cookie gets as far as the question and no further.
    /// </summary>
    [Fact]
    public async Task Delete_AsksAnAccountWithASecondFactorForItsCode()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory);

        FeedPost post = await StatusAsync(factory);

        // Enrolment through the service the route reads, rather than a flag of the test's own: the
        // gate asks `IsEnabledAsync`, and this is what makes that true.
        (
            await factory.Mfa.ConfirmEnrolmentAsync(
                FakeAuthService.AccountId,
                "SECRET",
                FakeMfaService.ValidCode
            )
        )
            .Should()
            .BeTrue();

        using HttpClient client = factory.CreateAuthenticatedClient();

        (await DeleteAsync(client, post.Id, null)).Should().Be("pocket.auth.mfa_required");
        (await DeleteAsync(client, post.Id, "000000")).Should().Be("pocket.auth.invalid_code");

        // Still there: neither refusal deleted anything on its way past.
        (await Service(factory).GetPostAsync(post.Id, null, default))
            .Should()
            .NotBeNull();

        (await DeleteAsync(client, post.Id, FakeMfaService.ValidCode)).Should().BeNull();
        (await Service(factory).GetPostAsync(post.Id, null, default)).Should().BeNull();
    }

    /// <summary>The refusal code, or null when the route answered 200.</summary>
    private static async Task<string?> DeleteAsync(HttpClient client, int postId, string? code)
    {
        using HttpResponseMessage response = await client.DeleteAsync(
            $"/api/user/feed/{postId}" + (code is null ? "" : $"?code={code}")
        );

        if (response.StatusCode == HttpStatusCode.OK)
        {
            return null;
        }

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        return JsonDocument
            .Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("error")
            .GetString();
    }

    private static IWebApiFeedService Service(WebApiTestFactory factory) =>
        new WebApiFeedService(factory.DbContexts, factory.Events, factory.WordFilter);

    private static async Task<FeedPost> StatusAsync(WebApiTestFactory factory) =>
        (
            await Service(factory)
                .PublishAsync(
                    SignedInPlayerId,
                    new FeedPublishRequest(FeedPostKind.Status, "coucou", null, null, null, null),
                    default
                )
        ).Post!;

    /// <summary>
    /// The avatar <see cref="WebApiTestFactory.CreateAuthenticatedClient" /> resolves to.
    /// <c>FakePlayerService</c> answers the account's avatar list with exactly this one, so a post
    /// the signed-in client may delete has to belong to it.
    /// </summary>
    private const int SignedInPlayerId = 100;

    private static async Task PlayerAsync(WebApiTestFactory factory)
    {
        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        db.Players.Add(
            new PlayerEntity
            {
                Id = SignedInPlayerId,
                Name = "Tester",
                Figure = "hr-115",
                Gender = AvatarGenderType.Male,
                PlayerStatus = PlayerStatusType.Offline,
                PlayerPerks = PlayerPerkFlags.None,
                ProfileVisible = true,
            }
        );

        await db.SaveChangesAsync();
    }
}

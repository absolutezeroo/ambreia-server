using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Feed;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Vortex.WebApi.Services;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// The hotel-wide word filter on the website's feed: a refused post is never written, a replaced
/// one is written replaced, and a poll choice is filtered like the body.
/// </summary>
public sealed class FeedWordFilterTests
{
    private const int SignedInPlayerId = 100;

    [Fact]
    public async Task ARefusedBody_IsNotWritten_AndAnswersWordFiltered()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory);
        factory.WordFilter.Answer = text => new WordFilterResult(text, true, false);

        (FeedRefusal refusal, FeedPost? post) = await Service(factory)
            .PublishAsync(
                SignedInPlayerId,
                new FeedPublishRequest(FeedPostKind.Status, "con", null, null, null),
                default
            );

        refusal.Should().Be(FeedRefusal.WordFiltered);
        post.Should().BeNull();

        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();
        (await db.WebFeedPosts.CountAsync()).Should().Be(0);
        factory
            .WordFilter.Applied.Should()
            .ContainSingle()
            .Which.Should()
            .Be(("con", WordFilterSource.FeedPost, SignedInPlayerId));
    }

    [Fact]
    public async Task AReplacedBody_IsStoredReplaced()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory);
        factory.WordFilter.Answer = text =>
            text == "t'es con"
                ? new WordFilterResult("t'es bobba", false, true)
                : new WordFilterResult(text, false, false);

        (FeedRefusal refusal, FeedPost? post) = await Service(factory)
            .PublishAsync(
                SignedInPlayerId,
                new FeedPublishRequest(FeedPostKind.Status, "t'es con", null, null, null),
                default
            );

        refusal.Should().Be(FeedRefusal.None);
        post!.Body.Should().Be("t'es bobba");
    }

    [Fact]
    public async Task ARefusedPollChoice_RefusesThePost()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory);
        factory.WordFilter.Answer = text => new WordFilterResult(text, text == "con", false);

        (FeedRefusal refusal, _) = await Service(factory)
            .PublishAsync(
                SignedInPlayerId,
                new FeedPublishRequest(FeedPostKind.Poll, "Qui ?", ["toi", "con"], null, null),
                default
            );

        refusal.Should().Be(FeedRefusal.WordFiltered);
    }

    /// <summary>
    /// "bobba" is longer than the word it replaces, so a body at the limit can cross it. The
    /// answer is the one the length check already gives, not a row over the column.
    /// </summary>
    [Fact]
    public async Task AReplacementThatPushesTheBodyPastItsLimit_IsTooLong()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory);
        factory.WordFilter.Answer = text => new WordFilterResult(text + "xxxxxx", false, true);

        (FeedRefusal refusal, _) = await Service(factory)
            .PublishAsync(
                SignedInPlayerId,
                new FeedPublishRequest(FeedPostKind.Status, new string('a', 500), null, null, null),
                default
            );

        refusal.Should().Be(FeedRefusal.BodyTooLong);
    }

    [Fact]
    public async Task ThePublishRoute_Answers400WordFiltered()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory);
        factory.WordFilter.Answer = text => new WordFilterResult(text, true, false);

        using HttpClient client = factory.CreateAuthenticatedClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/user/feed",
            new { kind = 0, body = "con" }
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonDocument
            .Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("error")
            .GetString()
            .Should()
            .Be("feed_word_filtered");
    }

    private static IWebApiFeedService Service(WebApiTestFactory factory) =>
        new WebApiFeedService(factory.DbContexts, factory.Events, factory.WordFilter);

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

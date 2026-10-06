using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Feed;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Vortex.WebApi.Services;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// Hashtags: pulled out of the body on publish, and the filter that reads them back.
/// </summary>
/// <remarks>
/// The tag is stored lowercased because that is the search key, while the body keeps the casing
/// the author typed — so <c>#McBurgerFC</c> reads as they wrote it and still finds
/// <c>#mcburgerfc</c>. The pattern is duplicated on the website, in
/// <c>components/feed/FeedBody.svelte</c>, because the page has to know where to break the text to
/// make the links; the two are kept in step by hand and a mismatch is visible rather than silent —
/// a tag that renders plain, or a link that finds nothing.
/// </remarks>
public sealed class FeedTagTests
{
    [Theory]
    [InlineData("rien ici", 0)]
    [InlineData("regarde #coolHashtag", 1)]
    [InlineData("#a #b #a", 2)]
    [InlineData("#Pizza et #pizza", 1)]
    [InlineData("#accentué", 1)]
    [InlineData("c#, f# et le reste", 0)]
    public void ExtractFindsEachTagOnce(string body, int expected)
    {
        FeedTags.Extract(body).Should().HaveCount(expected);
    }

    [Fact]
    public void ExtractLowercases()
    {
        FeedTags.Extract("#McBurgerFC").Should().Equal("mcburgerfc");
    }

    [Fact]
    public async Task PublishingStoresTheTagsAndTheFeedFiltersOnThem()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(
                    FeedPostKind.Status,
                    "photos #coolHashtag",
                    null,
                    null,
                    null
                ),
                default
            );

        await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(FeedPostKind.Status, "rien", null, null, null),
                default
            );

        FeedPage filtered = await Service(factory).GetFeedAsync(1, 0, "coolhashtag", null, default);

        filtered.Items.Should().ContainSingle();
        filtered.Items[0].Tags.Should().Equal("coolhashtag");
        filtered.Total.Should().Be(1, "the count has to narrow with the page, or the pager lies");
    }

    [Fact]
    public async Task TheFilterAcceptsTheTagWrittenWithItsHash()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(FeedPostKind.Status, "essai #vortex", null, null, null),
                default
            );

        // The links the page builds carry the bare word, but a player typing in the search box
        // will type the `#` they can see. Both have to arrive at the same feed.
        (await Service(factory).GetFeedAsync(1, 0, "#VORTEX", null, default))
            .Items.Should()
            .ContainSingle();
    }

    [Fact]
    public async Task ATagIsSharedBetweenPostsRatherThanDuplicated()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(FeedPostKind.Status, "un #tag", null, null, null),
                default
            );

        await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(FeedPostKind.Status, "deux #tag", null, null, null),
                default
            );

        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        (await db.WebFeedTags.CountAsync()).Should().Be(1);
        (await db.WebFeedPostTags.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ACommentsTagsAreExtractedToo()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        FeedPost root = (
            await Service(factory)
                .PublishAsync(
                    1,
                    new FeedPublishRequest(FeedPostKind.Status, "coucou", null, null, null),
                    default
                )
        ).Post!;

        // NAMED, not positional: `ParentId` and `GroupId` are adjacent `int?`, and putting the
        // root's id in the wrong one is refused as `GroupNotMember` — which shows up as a null
        // post and a NullReferenceException three lines later, not as anything about groups.
        FeedPost comment = (
            await Service(factory)
                .PublishAsync(
                    1,
                    new FeedPublishRequest(
                        Kind: FeedPostKind.Status,
                        Body: "et hop #reponse",
                        PollChoices: null,
                        ParentId: root.Id,
                        GroupId: null
                    ),
                    default
                )
        ).Post!;

        // A comment is a post, so it carries tags like one — but the FEED lists roots only, so a
        // tag that lives only in a comment must not pull its root into a filtered page.
        comment.Tags.Should().Equal("reponse");
        (await Service(factory).GetFeedAsync(1, 0, "reponse", null, default))
            .Items.Should()
            .BeEmpty();
    }

    private static IWebApiFeedService Service(WebApiTestFactory factory) =>
        new WebApiFeedService(factory.DbContexts, factory.Events, factory.WordFilter);

    private static async Task PlayerAsync(WebApiTestFactory factory, int id, string name)
    {
        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        db.Players.Add(
            new PlayerEntity
            {
                Id = id,
                Name = name,
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
